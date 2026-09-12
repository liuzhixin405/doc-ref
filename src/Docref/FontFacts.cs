namespace Docref;

/// <summary>字体名相关的判断。PdfPig 的字体名带子集前缀（如 <c>AAAAAF+Consolas</c>），因此一律用包含匹配。</summary>
public static class FontFacts
{
    private static readonly string[] MonospaceNames =
        ["Consolas", "Courier", "Menlo", "Monaco", "Mono"];

    /// <summary>Microsoft Learn 用 docons 画小图标。发现其他图标字体时往这里加。</summary>
    private static readonly string[] IconFontNames = ["docons"];

    /// <summary>是否等宽字体。技术文档里的代码几乎总是等宽，这是识别代码最可靠的信号。</summary>
    public static bool IsMonospace(string font)
        => MonospaceNames.Any(name => font.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 是否图标字体。图标字形不是文字 —— 真实输出里 docons 的字形污染成了 "ﾉExpand table"。
    /// </summary>
    public static bool IsIconFont(string font)
        => IconFontNames.Any(name => font.Contains(name, StringComparison.OrdinalIgnoreCase));
}
