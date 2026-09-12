using Docref;

namespace Docref.Tests;

/// <summary>
/// 代码块里 PDF 排版折行的还原。
///
/// 几何数据全部来自 <c>dotnet-csharp.pdf</c> 第 760 页实测
/// （<c>docref debug-lines "D:\pdf\dotnet-csharp.pdf" 760</c>）。
/// 该页代码块左缘 X=77.5，块内最大右缘 498.9，字号 10.5。
///
/// 关键事实：**所有代码行的 X 都是 77.5** —— 缩进在文本里是真空格，不在坐标里。
/// 所以「续行顶到左边缘」这类判据不成立。真正的判据是贪心换行的逆运算：
/// 上一行右缘 + 本行宽度 &gt; 右边距。
/// </summary>
public class CodeWrapTests
{
    private const double Left = 77.5;
    private const double Margin = 498.9;

    /// <summary>页宽 595.9 − 代码左缘 77.5。真实页面的代码区右边距。</summary>
    private const double PageMargin = 595.9 - Left;

    /// <summary>
    /// 实测折行：注释里的长 URL 被折成两行，取出的代码因此不可编译。
    ///   604.1  右缘 498.9  "    // https://…/creating-a-personal-access-token-"
    ///   589.9  右缘 296.9  "for-the-command-line/#creating-a-token"
    /// 498.9 + (296.9-77.5) = 718 > 498.9 → 是折行，必须接回同一行。
    /// </summary>
    [Fact]
    public void Rejoins_a_line_that_pdf_layout_wrapped()
    {
        var lines = new[]
        {
            Code(604.1, "    // https://help.github.com/articles/creating-a-personal-access-token-", 498.9),
            Code(589.9, "for-the-command-line/#creating-a-token", 296.9),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 760, lines, PageMargin));

        Assert.Equal(
            "    // https://help.github.com/articles/creating-a-personal-access-token-for-the-command-line/#creating-a-token",
            block.Text);
    }

    /// <summary>
    /// 折行处的空格属于上一行，接回时不能再补空格。实测：
    ///   433.1  右缘 296.9  "    var client = new "
    ///   418.9  右缘 337.3  "Octokit.ProductHeaderValue(\"IssueQueryDemo\"))"
    /// 注意上一行右缘只有 296.9，远离右边距 —— 是因为下一段太宽放不下才折的。
    /// 「上一行必须顶到右边距」这个判据在这里会失效。
    /// </summary>
    [Fact]
    public void Rejoins_without_inserting_an_extra_space()
    {
        var lines = new[]
        {
            Code(433.1, "    var client = new ", 296.9),
            Code(418.9, "Octokit.ProductHeaderValue(\"IssueQueryDemo\"))", 337.3),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 760, lines, PageMargin));

        Assert.Equal("    var client = new Octokit.ProductHeaderValue(\"IssueQueryDemo\"))", block.Text);
    }

    /// <summary>
    /// 护栏：有前导缩进的行永远不是续行 —— 缩进在文本里，而排版折出来的续行没有缩进。
    /// 实测这一对若只看宽度会被误判（296.9 + 392.6 = 689 &gt; 498.9）：
    ///   589.9  右缘 296.9  "for-the-command-line/#creating-a-token"
    ///   575.6  右缘 470.1  "    //Select the following permissions for your GitHub Access Token:"
    /// </summary>
    [Fact]
    public void Does_not_rejoin_a_line_that_has_leading_indentation()
    {
        var lines = new[]
        {
            Code(589.9, "for-the-command-line/#creating-a-token", 296.9),
            Code(575.6, "    //Select the following permissions for your GitHub Access Token:", 470.1),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 760, lines, PageMargin));

        Assert.Equal(
            "for-the-command-line/#creating-a-token\n    //Select the following permissions for your GitHub Access Token:",
            block.Text);
    }

    /// <summary>
    /// 护栏：上一行以 <c>;</c> / <c>{</c> / <c>}</c> 结尾时不是折行 ——
    /// 排版不会在语句结束处折行。没有这条，恰好顶到右边距的语句后面跟一个无缩进的
    /// <c>}</c> 就会被粘上去，产出 <c>};var x = 1;</c> 这种坏代码。
    /// </summary>
    [Fact]
    public void Does_not_rejoin_after_a_statement_terminator()
    {
        var lines = new[]
        {
            Code(646.9, "var veryLongStatementThatExactlyFillsTheAvailableWidth = 1;", Margin),
            Code(632.6, "}", 83.3),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 760, lines, PageMargin));

        Assert.Equal("var veryLongStatementThatExactlyFillsTheAvailableWidth = 1;\n}", block.Text);
    }

    /// <summary>
    /// 护栏：放得下就不是折行。实测 <c>{</c> 紧跟在 <c>static async Task Main(...)</c> 之后：
    /// 291.1 + (83.3-77.5) = 296.9 &lt; 498.9 → 放得下 → 是真换行。
    /// </summary>
    [Fact]
    public void Does_not_rejoin_when_the_next_line_would_have_fit()
    {
        var lines = new[]
        {
            Code(646.9, "static async Task Main(string[] args)", 291.1),
            Code(632.6, "{", 83.3),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 760, lines, PageMargin));

        Assert.Equal("static async Task Main(string[] args)\n{", block.Text);
    }

    /// <summary>
    /// 真实假阳性：<c>dotnet-navigate-advanced-programming.pdf</c> 第 474 页是 Northwind 的
    /// CSV 数据样本（等宽块，所以归类为 code）。每行都是一条完整记录，但宽度测试全部满足 ——
    /// 因为上一行本就接近右边距，任何后续行都"放不下"：
    ///   805.1 右缘 475.8  GALED,…    →  475.8 + 317.5 = 793 &gt; 518.4
    ///   790.9 右缘 395.0  GODOS,…
    /// 结果 28 条记录被粘成一行 3184 字符。
    ///
    /// 几何本身区分不了「恰好接近满行的完整行」和「真折行」。但块层面能：
    /// p760 代码块 30 行只有 5 处折行（17%），而这里 100% 都满足 ——
    /// **比例过高说明这个块不是折行的代码，几何推断不适用，整块都不接回。**
    /// </summary>
    [Fact]
    public void Does_not_rejoin_a_block_where_almost_every_break_looks_like_a_wrap()
    {
        var lines = new[]
        {
            Code(805.1, "GALED,Galería del gastrónomo,Rambla de Cataluña, 23,Barcelona,08022", 475.8),
            Code(790.9, "GODOS,Godos Cocina Típica,C/ Romero, 33,Sevilla,41101", 395.0),
            Code(776.6, "GOURL,Gourmet Lanchonetes,Av. Brasil, 442,Campinas,04876-786", 435.4),
            Code(762.4, "GREAL,Great Lakes Food Market,2732 Baker Blvd.,Eugene,97403", 429.6),
            Code(748.1, "GROSR,GROSELLA-Restaurante,5ª Ave. Los Palos Grandes,Caracas,1081", 464.3),
            Code(733.9, "HANAR,Hanari Carnes,Rua do Paço, 67,Rio de Janeiro,05454-876", 435.4),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 474, lines, PageMargin));

        Assert.Equal(6, block.Text!.Split('\n').Length);
    }

    /// <summary>
    /// 真实假阳性：连续的注释行被粘在一起，产出
    /// <c>…like the following://     Task #0 created at…</c>。
    ///
    /// 几何来自 <c>dotnet-navigate-advanced-programming.pdf</c> 第 194 页
    /// （代码左缘 53.5，故边距 595.9 − 53.5 = 542.4）：
    ///   161.6  右缘 342.1  "// The example displays output like the following:"
    ///   147.4  右缘 417.2  "//     Task #0 created at 635116412924597583, ran on thread #3."
    /// 342.1 + 363.7 = 705.8 &gt; 542.4，缩进护栏与语句结束护栏都拦不住。
    ///
    /// 判据：**续行不可能以注释标记开头** —— 排版折行不会在断点插入 <c>//</c>。
    /// </summary>
    [Fact]
    public void Does_not_rejoin_a_line_that_starts_with_a_comment_marker()
    {
        const double left = 53.5;
        const double margin = 595.9 - left;

        var lines = new[]
        {
            CodeAt(left, 161.6, "// The example displays output like the following:", 342.1),
            CodeAt(left, 147.4, "//     Task #0 created at 635116412924597583, ran on thread #3.", 417.2),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 194, lines, margin));

        Assert.Equal(
            "// The example displays output like the following:\n"
            + "//     Task #0 created at 635116412924597583, ran on thread #3.",
            block.Text);
    }

    private static TextLine CodeAt(double left, double y, string text, double right)
        => new(y, [new TextRun(text, "AAAAAF+Consolas", 10.5, left, y, right - left)]);

    /// <summary>代码块行距实测 14.3pt（646.9 → 632.6），远小于空行阈值，不会被判成空行。</summary>
    private static TextLine Code(double y, string text, double right)
        => new(y, [new TextRun(text, "AAAAAF+Consolas", 10.5, Left, y, right - Left)]);
}
