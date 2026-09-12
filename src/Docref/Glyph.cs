namespace Docref;

/// <summary>
/// 页面上的单个字符及其字体与位置。
///
/// 这是隔离 PdfPig 的边界类型：只有一个薄适配器负责把 PdfPig 的 <c>Letter</c> 映射成它，
/// 之后所有逻辑都只认 <see cref="Glyph"/>，因此全部可以脱离 PDF 单测。
/// </summary>
/// <param name="Width">字符的推进宽度。用于判断相邻字符在 X 方向是否真的连着 —— 缺了它就
/// 无法区分「紧挨着」和「隔着一段行内代码」。</param>
public sealed record Glyph(string Value, string Font, double Size, double X, double Y, double Width);
