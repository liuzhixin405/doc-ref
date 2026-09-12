using Docref;

namespace Docref.Tests;

public class InlineMarkdownTests
{
    /// <summary>
    /// 等宽字体的 run 是行内代码，渲染成 Markdown 反引号。
    /// 这样 text 字段本身就是可读的 Markdown，行内代码信息不丢失，
    /// 下游也能用正则从反引号里取出符号候选。
    /// </summary>
    [Fact]
    public void Wraps_monospace_runs_in_backticks()
    {
        var line = Line(
            Run("The "),
            Run("dynamic", font: "Consolas", size: 10.2),
            Run(" type"));

        Assert.Equal("The `dynamic` type", InlineMarkdown.Render(line));
    }

    /// <summary>
    /// Learn 的 PDF 在行内代码后面塞了填充空格（真实数据：'  type is similar to '，两个前导空格），
    /// 直接拼接会留下双空格。
    /// </summary>
    [Fact]
    public void Collapses_padding_spaces_around_inline_code()
    {
        var line = Line(
            Run("The "),
            Run("dynamic", font: "Consolas", size: 10.2),
            Run("  type is similar to "));

        Assert.Equal("The `dynamic` type is similar to", InlineMarkdown.Render(line));
    }

    /// <summary>
    /// 同样的填充空格出现在标点前面（真实数据：' . The main differences are:'），
    /// 会得到 "`object` ." 这种脏文本。
    /// </summary>
    [Fact]
    public void Removes_space_before_punctuation()
    {
        var line = Line(
            Run("similar to "),
            Run("object", font: "Consolas", size: 10.2),
            Run(" . The main differences are:"));

        Assert.Equal("similar to `object`. The main differences are:", InlineMarkdown.Render(line));
    }

    /// <summary>
    /// 真实缺陷：表格两列同基线、同字体，<see cref="GlyphMerger"/> 按 X 间隙正确切成了两个 run，
    /// 但渲染时直接拼接，得到 "boolSystem.Boolean"、"byteSystem.Byte"、"C# type keyword.NET type"
    /// 这种损坏数据。列间隙必须还原成分隔空格。
    /// </summary>
    [Fact]
    public void Separates_runs_that_are_far_apart_horizontally()
    {
        var line = Line(
            Run("bool", x: 46, width: 22),
            Run("System.Boolean", x: 250, width: 80));

        Assert.Equal("bool System.Boolean", InlineMarkdown.Render(line));
    }

    /// <summary>
    /// 边界护栏（合成数据，非实测复现）：几个 pt 的字间微距不该被当成列边界。
    ///
    /// 说明来历：我最初把 "已启 用" 误诊为字间微距被误判，为此把补空格的门槛从
    /// GlyphMerger 的 0.3 提到了一个整字宽。后来查明真因是中文折行处补了空格
    /// （见 <c>BlockClassifierTests.Joins_wrapped_chinese_lines_without_a_space</c>）。
    /// 这条测试的前提是我编的数字，不是实测 —— 但它锁住的性质本身是对的：
    /// 表格列间隙有上百 pt，用一个整字宽当门槛比 0.3 稳健，所以保留。
    /// </summary>
    [Fact]
    public void Does_not_separate_characters_a_few_points_apart()
    {
        var line = Line(
            Run("已启", x: 46.0, width: 20.6, font: "MicrosoftYaHei", size: 10.3),
            Run("用", x: 70.0, width: 10.3, font: "MicrosoftYaHei", size: 10.3));

        Assert.Equal("已启用", InlineMarkdown.Render(line));
    }

    private static TextLine Line(params TextRun[] runs) => new(runs[0].Y, runs);

    private static TextRun Run(string text, double x = 46, string font = "SegoeUI", double size = 12.0,
                               double width = 0)
        => new(text, font, size, x, 411.3, width);
}
