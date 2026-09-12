namespace Docref;

/// <summary>同一基线上的若干 <see cref="TextRun"/>，按 X 升序排列。</summary>
public sealed record TextLine(double Y, IReadOnlyList<TextRun> Runs)
{
    /// <summary>行首的 X 坐标。</summary>
    public double X => Runs.Count == 0 ? 0 : Runs[0].X;

    /// <summary>行末的 X 坐标。用于判断 PDF 排版是否在此处折了行。</summary>
    public double Right => Runs.Count == 0 ? 0 : Runs.Max(r => r.X + r.Width);

    public string Text => string.Concat(Runs.Select(r => r.Text));
}
