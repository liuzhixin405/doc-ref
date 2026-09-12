namespace Docref;

/// <summary>把散落的 <see cref="TextRun"/> 聚合成有阅读顺序的 <see cref="TextLine"/>。</summary>
public static class LineBuilder
{
    /// <summary>
    /// 基线聚类容差（pt）。必须大于行内代码相对正文的基线偏移（实测 3.6pt），
    /// 又必须小于正常行距（实测约 19pt）。
    /// </summary>
    private const double BaselineTolerance = 5.0;

    public static IReadOnlyList<TextLine> Build(IEnumerable<TextRun> runs)
    {
        var lines = new List<TextLine>();
        var current = new List<TextRun>();
        var referenceY = 0.0;

        foreach (var run in runs.OrderByDescending(r => r.Y))
        {
            if (current.Count > 0 && Math.Abs(run.Y - referenceY) > BaselineTolerance)
            {
                lines.Add(Close(current, referenceY));
                current = [];
            }

            if (current.Count == 0)
            {
                referenceY = run.Y;
            }

            current.Add(run);
        }

        if (current.Count > 0)
        {
            lines.Add(Close(current, referenceY));
        }

        return lines;

        static TextLine Close(List<TextRun> runs, double y)
            => new(y, runs.OrderBy(r => r.X).ToList());
    }
}
