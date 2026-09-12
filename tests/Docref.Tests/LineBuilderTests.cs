using Docref;

namespace Docref.Tests;

public class LineBuilderTests
{
    /// <summary>
    /// PDF 坐标系 Y 向上：y=700 在页面上方，y=100 在下方。
    /// PdfPig 给出的字符顺序不保证是阅读顺序（真实文档里代码块会被排到整页最后），
    /// 所以必须按 Y 降序重排，否则正文顺序会静默错乱。
    /// </summary>
    [Fact]
    public void Orders_lines_top_to_bottom()
    {
        var runs = new[]
        {
            Run("在下面", y: 100),
            Run("在上面", y: 700),
        };

        var lines = LineBuilder.Build(runs);

        Assert.Equal(new[] { "在上面", "在下面" }, lines.Select(l => l.Text));
    }

    /// <summary>
    /// 语法高亮会把同一行代码切成多个 run（真实数据：'int' 与 ' a = 123;' 同基线 y=362.6，
    /// x 分别是 53.5 和 70.8）。必须按 X 升序合并回一行，且不依赖输入顺序。
    /// </summary>
    [Fact]
    public void Joins_runs_on_the_same_baseline_in_x_order()
    {
        var runs = new[]
        {
            Run(" a = 123;", y: 362.6, x: 70.8),
            Run("int",       y: 362.6, x: 53.5),
        };

        var lines = LineBuilder.Build(runs);

        var line = Assert.Single(lines);
        Assert.Equal("int a = 123;", line.Text);
    }

    /// <summary>
    /// 行内代码的基线与周围正文不齐。真实数据：
    ///   SegoeUI @12.0 y=411.3 'The '
    ///   Consolas@10.2 y=414.9 'dynamic'      ← 偏移 3.6pt
    ///   SegoeUI @12.0 y=411.3 ' type is similar to'
    /// 三者视觉上是同一行，必须归为一行，否则行内代码会被拆成独立的一行。
    /// </summary>
    [Fact]
    public void Groups_inline_code_whose_baseline_is_slightly_offset()
    {
        var runs = new[]
        {
            Run("The ",                y: 411.3, x: 46.0),
            Run("dynamic",             y: 414.9, x: 70.7, font: "Consolas", size: 10.2),
            Run(" type is similar to", y: 411.3, x: 109.9),
        };

        var lines = LineBuilder.Build(runs);

        var line = Assert.Single(lines);
        Assert.Equal("The dynamic type is similar to", line.Text);
    }

    /// <summary>
    /// 容差的另一侧：正常行距约 19pt（真实数据 y=411.3 与 y=430.5），必须保持为两行。
    /// 这条与 <see cref="Groups_inline_code_whose_baseline_is_slightly_offset"/> 共同把容差
    /// 夹在 3.6 与 19 之间。
    /// </summary>
    [Fact]
    public void Keeps_adjacent_lines_separate_at_normal_line_spacing()
    {
        var runs = new[]
        {
            Run("第二行", y: 411.3),
            Run("第一行", y: 430.5),
        };

        var lines = LineBuilder.Build(runs);

        Assert.Equal(new[] { "第一行", "第二行" }, lines.Select(l => l.Text));
    }

    private static TextRun Run(string text, double y, double x = 46, string font = "SegoeUI", double size = 12.0)
        => new(text, font, size, x, y);
}
