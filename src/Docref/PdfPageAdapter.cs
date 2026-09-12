using UglyToad.PdfPig.Content;

namespace Docref;

/// <summary>
/// 唯一一处把 PdfPig 的类型映射成本项目模型的地方。刻意保持无逻辑 ——
/// 逻辑都在只认 <see cref="Glyph"/> 的那些类里，所以能脱离 PDF 单测。
/// </summary>
internal static class PdfPageAdapter
{
    public static IReadOnlyList<Glyph> ReadGlyphs(Page page)
    {
        var glyphs = new List<Glyph>(page.Letters.Count);

        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrEmpty(letter.Value))
            {
                continue;
            }

            // 图标字体的字形不是文字。真实输出里 docons 曾污染成 "ﾉExpand table"。
            if (FontFacts.IsIconFont(letter.FontName ?? string.Empty))
            {
                continue;
            }

            glyphs.Add(new Glyph(
                Value: letter.Value,
                // 字体名带子集前缀（如 AAAAAF+Consolas），原样保留：
                // FontFacts 用包含匹配，剥前缀对结果没有影响。
                Font: letter.FontName ?? string.Empty,
                Size: letter.PointSize,
                X: letter.StartBaseLine.X,
                Y: letter.StartBaseLine.Y,
                Width: letter.EndBaseLine.X - letter.StartBaseLine.X));
        }

        return glyphs;
    }
}
