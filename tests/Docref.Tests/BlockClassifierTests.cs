using Docref;

namespace Docref.Tests;

/// <summary>
/// 坐标全部用 PdfPig 的 PDF 原生坐标系（Y 向上，页高 842.9）。
/// 注释里标注的 "Learn 实测" 值来自 dotnet-csharp-language-reference.pdf 第 11 页，
/// 原始探测用的是 Y 向下坐标，此处已换算为 842.9 - y。
/// </summary>
public class BlockClassifierTests
{
    /// <summary>
    /// 判断代码块的可靠依据是「整行 100% 等宽字体」，而不是字号
    /// （行内代码 10.2 与代码块 10.5 只差 0.3，太脆）。连续的代码行要合并成一个块并保留换行。
    /// Learn 实测：'int a = 123;' 在 y≈480.3，'System.Int32 b = 123;' 在 y≈466.1。
    /// </summary>
    [Fact]
    public void Groups_consecutive_monospace_lines_into_one_code_block()
    {
        var lines = new[]
        {
            Line(480.3, Code("int a = 123;")),
            Line(466.1, Code("System.Int32 b = 123;")),
        };

        var blocks = BlockClassifier.Classify(page: 11, lines);

        var block = Assert.Single(blocks);
        Assert.Equal("code", block.Kind);
        Assert.Equal("int a = 123;\nSystem.Int32 b = 123;", block.Text);
    }

    /// <summary>正文行与代码行必须归入不同的块，各自 kind 正确，且保持阅读顺序。</summary>
    [Fact]
    public void Separates_body_text_from_code()
    {
        var lines = new[]
        {
            Line(535.0, Body("declare variables of the same type:")),
            Line(480.3, Code("int a = 123;")),
        };

        var blocks = BlockClassifier.Classify(page: 11, lines);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(Block.KindText, blocks[0].Kind);
        Assert.Equal("declare variables of the same type:", blocks[0].Text);
        Assert.Equal(Block.KindCode, blocks[1].Kind);
        Assert.Equal("int a = 123;", blocks[1].Text);
    }

    /// <summary>
    /// 一个段落在 PDF 里被拆成多行，要合回一段并用空格连接（不是换行 —— 换行会让下游
    /// 误以为是硬换行）。Learn 实测正常行距 19.5pt。
    /// </summary>
    [Fact]
    public void Joins_wrapped_paragraph_lines_with_a_space()
    {
        var lines = new[]
        {
            Line(813.6, Body("The C# language reference documents the most")),
            Line(794.1, Body("recently released version of the language.")),
        };

        var blocks = BlockClassifier.Classify(page: 11, lines);

        var block = Assert.Single(blocks);
        Assert.Equal(
            "The C# language reference documents the most recently released version of the language.",
            block.Text);
    }

    /// <summary>
    /// 段落之间的间距明显大于行距，必须切成两个块 —— 否则整页正文会糊成一个块，
    /// 检索粒度就没了。Learn 实测：行距 19.5pt，段间距 31.5pt，阈值取中间。
    /// 这条与 <see cref="Joins_wrapped_paragraph_lines_with_a_space"/> 共同把阈值夹住。
    /// </summary>
    [Fact]
    public void Splits_blocks_at_paragraph_gap()
    {
        var lines = new[]
        {
            Line(775.3, Body("language release.")),
            Line(743.8, Body("The documentation identifies any feature")),
        };

        var blocks = BlockClassifier.Classify(page: 11, lines);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("language release.", blocks[0].Text);
        Assert.Equal("The documentation identifies any feature", blocks[1].Text);
    }

    /// <summary>
    /// 每个块要记下顶端 Y —— <see cref="SectionAssembler"/> 靠它把边界页劈开。
    /// 若不记（默认 0），所有块都会被判定在页面最底部，边界劈分整体失效。
    /// 取块内最靠上那行的基线；输入已是阅读顺序，所以就是第一行。
    /// </summary>
    [Fact]
    public void Records_the_block_top_position()
    {
        var lines = new[]
        {
            Line(813.6, Body("first line")),
            Line(794.1, Body("second line")),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 11, lines));

        Assert.Equal(813.6, block.Top);
    }

    /// <summary>
    /// 真实缺陷：一段完整代码被切成 4 块。代码行距实测 14.25pt，中间的空行造成 28.5pt 间隙，
    /// 超过 25pt 的段落阈值就被误切了。段落规则只适用于正文，不该管代码 ——
    /// 代码里的空行是排版的一部分，不是段落分隔。
    /// </summary>
    [Fact]
    public void Does_not_split_a_code_block_at_a_blank_line()
    {
        var lines = new[]
        {
            Line(601.13, Code("using System;")),
            Line(572.63, Code("public struct MutablePoint")),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 13, lines));

        Assert.Equal(Block.KindCode, block.Kind);
    }

    /// <summary>
    /// 空行本身要保留 —— 它分隔 using 段与类型声明，去掉就改变了代码的样子。
    /// 判据：行间隙约为两倍行距（10.5pt 的代码字号，行距 14.25pt，空行处 28.5pt）。
    /// </summary>
    [Fact]
    public void Preserves_blank_lines_inside_a_code_block()
    {
        var lines = new[]
        {
            Line(601.13, Code("using System;")),
            Line(572.63, Code("public struct MutablePoint")),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 13, lines));

        Assert.Equal("using System;\n\npublic struct MutablePoint", block.Text);
    }

    /// <summary>
    /// 反面：代码块内正常相邻的行（行距 14.25pt）不能被插入空行，否则代码全被撑散。
    /// 这条与上一条共同把「空行」阈值夹在 14.25 与 28.5 之间。
    /// </summary>
    [Fact]
    public void Does_not_insert_blank_lines_between_adjacent_code_lines()
    {
        var lines = new[]
        {
            Line(572.63, Code("public struct MutablePoint")),
            Line(558.38, Code("{")),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 13, lines));

        Assert.Equal("public struct MutablePoint\n{", block.Text);
    }

    /// <summary>
    /// 真实缺陷：标题字号大（中文文档实测 20.1pt），折行后行距约 26pt，
    /// 超过固定的 25pt 段落阈值就被切成两块 ——
    /// "HostBuilder 在开发环境中启用" / "ValidateOnBuild/ValidateScopes"。
    /// 段落阈值必须随字号缩放，固定值对大字号一定失效。
    /// </summary>
    [Fact]
    public void Does_not_split_a_wrapped_heading()
    {
        var lines = new[]
        {
            Line(795.4, Heading("HostBuilder 在开发环境中启用")),
            Line(769.3, Heading("ValidateOnBuild/ValidateScopes")),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 202, lines));

        Assert.Equal("HostBuilder 在开发环境中启用 ValidateOnBuild/ValidateScopes", block.Text);
    }

    /// <summary>
    /// 真实输出里 top 是 795.3799994511668 这种浮点噪声。8 万个块上白占几 MB，
    /// 也让 JSONL 难读。两位小数对「劈开边界页」这个用途绰绰有余。
    /// </summary>
    [Fact]
    public void Rounds_the_block_top_position()
    {
        var lines = new[] { Line(795.3799994511668, Body("x")) };

        var block = Assert.Single(BlockClassifier.Classify(page: 1, lines));

        Assert.Equal(795.38, block.Top);
    }

    /// <summary>
    /// 真实缺陷（实测复现）：中文文档里 "已启用。" 被折行成 "已启" + "用。"，
    /// 用空格连接折行就成了 "已启 用。"。
    /// 中文不用空格分词，折行处不能补空格 —— 而英文折行处必须补
    /// （见 <see cref="Joins_wrapped_paragraph_lines_with_a_space"/>，两条共同夹住这条规则）。
    /// </summary>
    [Fact]
    public void Joins_wrapped_chinese_lines_without_a_space()
    {
        var lines = new[]
        {
            Line(700.0, Chinese("ValidateScopes已启")),
            Line(680.5, Chinese("用。")),
        };

        var block = Assert.Single(BlockClassifier.Classify(page: 202, lines));

        Assert.Equal("ValidateScopes已启用。", block.Text);
    }

    private static TextLine Line(double y, params TextRun[] runs)
        => new(y, runs.Select(r => r with { Y = y }).ToList());

    private static TextRun Code(string text, double x = 53.5)
        => new(text, "AAAAAF+Consolas", 10.5, x, 0);

    private static TextRun Heading(string text, double x = 46)
        => new(text, "MicrosoftYaHei-Bold", 20.1, x, 0);

    private static TextRun Chinese(string text, double x = 46)
        => new(text, "MicrosoftYaHei", 10.3, x, 0);

    private static TextRun Body(string text, double x = 46)
        => new(text, "AAAAAC+SegoeUI", 12.0, x, 0);
}
