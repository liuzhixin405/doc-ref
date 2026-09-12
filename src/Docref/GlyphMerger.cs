namespace Docref;

/// <summary>
/// 把逐个字符的 <see cref="Glyph"/> 合并成样式连续的 <see cref="TextRun"/>。
///
/// 注意这里只按 (Y, X) 做局部排序，不试图还原跨基线的阅读顺序 —— 行内代码的基线
/// 与正文差几个 pt，跨基线排序会打乱顺序。真正的阅读顺序由 <see cref="LineBuilder"/>
/// 的基线容差聚类还原。
/// </summary>
public static class GlyphMerger
{
    public static IReadOnlyList<TextRun> Merge(IEnumerable<Glyph> glyphs)
    {
        var runs = new List<TextRun>();
        var buffer = new List<Glyph>();

        foreach (var glyph in glyphs.OrderByDescending(g => g.Y).ThenBy(g => g.X))
        {
            if (buffer.Count > 0 && !SameRun(buffer[^1], glyph))
            {
                runs.Add(Close(buffer));
                buffer = [];
            }

            buffer.Add(glyph);
        }

        if (buffer.Count > 0)
        {
            runs.Add(Close(buffer));
        }

        return runs;
    }

    /// <summary>
    /// 允许的字距误差，按字号比例。超过就说明中间隔着别的东西（行内代码，或表格的列边界）。
    /// <see cref="InlineMarkdown"/> 用同一个阈值判断要不要补分隔空格 —— 切分与还原必须同一把尺。
    /// </summary>
    internal const double KerningTolerance = 0.3;

    private static bool SameRun(Glyph previous, Glyph next)
        => previous.Font == next.Font
        && previous.Size.Equals(next.Size)
        && previous.Y.Equals(next.Y)
        && next.X - (previous.X + previous.Width) <= previous.Size * KerningTolerance;

    private static TextRun Close(List<Glyph> glyphs)
    {
        var first = glyphs[0];
        var last = glyphs[^1];

        return new TextRun(
            Text: string.Concat(glyphs.Select(g => g.Value)),
            Font: first.Font,
            Size: first.Size,
            X: first.X,
            Y: first.Y,
            // 末字符右缘 − 首字符左缘。InlineMarkdown 靠它判断 run 之间有没有真实间隙。
            Width: last.X + last.Width - first.X);
    }
}
