namespace Docref;

/// <summary>
/// 一段字体、字号连续的文本，由 PDF 页面上的字符聚合而来。
/// X / Y 取该段起始字符的基线位置（PDF 坐标系，Y 向上）。
/// </summary>
/// <param name="Width">这段文本的水平跨度。用于还原 run 之间的间隙 —— 缺了它，
/// 表格两列会被直接拼成 "boolSystem.Boolean"。</param>
public sealed record TextRun(string Text, string Font, double Size, double X, double Y, double Width = 0);
