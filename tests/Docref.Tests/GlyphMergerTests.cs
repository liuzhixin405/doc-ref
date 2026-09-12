using Docref;

namespace Docref.Tests;

public class GlyphMergerTests
{
    /// <summary>
    /// PdfPig 给出的是逐个字符。字体、字号、基线相同的相邻字符要合并成一个 run，
    /// 否则后面每一步都要处理成千上万个单字符片段。
    /// </summary>
    [Fact]
    public void Merges_glyphs_of_the_same_style_into_one_run()
    {
        var glyphs = new[]
        {
            G("i", x: 53.5),
            G("n", x: 58.0),
            G("t", x: 62.5),
        };

        var runs = GlyphMerger.Merge(glyphs);

        var run = Assert.Single(runs);
        Assert.Equal("int", run.Text);
        Assert.Equal(53.5, run.X);
    }

    /// <summary>
    /// 字体一变就必须切成新的 run —— 否则 <see cref="InlineMarkdown"/> 无从知道
    /// 哪一段是行内代码。真实数据：'The ' 是 SegoeUI，紧接的 'dynamic' 是 Consolas。
    /// </summary>
    [Fact]
    public void Starts_a_new_run_when_the_font_changes()
    {
        var glyphs = new[]
        {
            G("T", x: 46.0, font: Sans, size: 12.0, width: 6.0),
            G("h", x: 52.0, font: Sans, size: 12.0, width: 6.0),
            G("e", x: 58.0, font: Sans, size: 12.0, width: 6.0),
            G("d", x: 70.7, font: Mono, size: 10.2, width: 5.3),
            G("y", x: 76.0, font: Mono, size: 10.2, width: 5.3),
        };

        var runs = GlyphMerger.Merge(glyphs);

        Assert.Equal(2, runs.Count);
        Assert.Equal("The", runs[0].Text);
        Assert.Equal("dy", runs[1].Text);
    }

    /// <summary>
    /// 同一视觉行上，两段正文被行内代码隔开：'The ' (SegoeUI) 'dynamic' (Consolas) 'ty' (SegoeUI)。
    /// 两段 SegoeUI 的字体、字号、基线全都相同，排序后还紧挨着 —— 只比对样式就会把它们
    /// 错误合并成一段，行内代码则被挤到行尾，正文语序被破坏。
    /// 所以判断是否同一 run 还必须要求 X 方向相邻。
    /// </summary>
    [Fact]
    public void Does_not_merge_same_style_glyphs_separated_by_a_gap()
    {
        var glyphs = new[]
        {
            G("T", x: 46.0, y: 431.6, font: Sans, size: 12.0, width: 7.3),
            G("h", x: 53.3, y: 431.6, font: Sans, size: 12.0, width: 6.7),
            G("e", x: 60.0, y: 431.6, font: Sans, size: 12.0, width: 6.0),
            G("d", x: 70.7, y: 428.0, font: Mono, size: 10.2, width: 6.1),
            G("t", x: 109.9, y: 431.6, font: Sans, size: 12.0, width: 5.5),
            G("y", x: 115.4, y: 431.6, font: Sans, size: 12.0, width: 6.0),
        };

        var runs = GlyphMerger.Merge(glyphs);

        Assert.Equal(3, runs.Count);
        Assert.Equal(["The", "ty", "d"], runs.Select(r => r.Text));
    }

    /// <summary>
    /// run 的宽度是 <see cref="InlineMarkdown"/> 判断间隙的依据。不记（默认 0）的话
    /// 表格列粘连那个缺陷在真实数据上修不掉。
    /// 末字符右缘 − 首字符左缘 = 62.5 + 4.5 − 53.5 = 13.5。
    /// </summary>
    [Fact]
    public void Records_the_run_width()
    {
        var runs = GlyphMerger.Merge([G("i", x: 53.5), G("n", x: 58.0), G("t", x: 62.5)]);

        Assert.Equal(13.5, Assert.Single(runs).Width, precision: 2);
    }

    private const string Sans = "AAAAAC+SegoeUI";
    private const string Mono = "AAAAAF+Consolas";

    private static Glyph G(string value, double x, double y = 480.3,
                           string font = Mono, double size = 10.5, double width = 4.5)
        => new(value, font, size, x, y, width);
}
