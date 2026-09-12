using System.Security.Cryptography;
using UglyToad.PdfPig;

namespace Docref;

/// <summary>
/// 提取流水线：字符 → run → 行 → 块 → section。
/// 不联网、不调 AI，全程确定性。
/// </summary>
public static class PdfExtractor
{
    public const string Version = "0.1.0";

    /// <param name="firstPage">起始页（1 起，含）。</param>
    /// <param name="lastPage">结束页（含）。null 表示到文档末尾。</param>
    /// <exception cref="NotSupportedException">PDF 没有文本层（扫描件）。</exception>
    public static ExtractedDocument Extract(string path, int firstPage = 1, int? lastPage = null)
    {
        using var document = PdfDocument.Open(path);

        if (!TextLayerProbe.HasTextLayer(document))
        {
            throw new NotSupportedException(
                $"{Path.GetFileName(path)} 没有文本层（扫描件），无法提取。本组件不做 OCR，" +
                "也不会返回空壳结果假装成功。");
        }

        var warnings = new List<string>();
        var outline = OutlineReader.Read(document);

        if (outline.Entries.Count == 0)
        {
            warnings.Add("文档没有大纲，无法划分 section。");
        }

        if (outline.Destinations.Count == 0)
        {
            warnings.Add("没有解析到任何命名目标，该文档可能用的是 /Names /Dests 名称树形式，暂不支持。");
        }

        var containers = outline.Entries.Count(e => e.Page is null);
        if (containers > 0)
        {
            warnings.Add($"{containers} 个大纲条目是纯容器节点（无目标页），只参与构成路径，不产出 section。");
        }

        var from = Math.Max(1, firstPage);
        var to = Math.Min(lastPage ?? document.NumberOfPages, document.NumberOfPages);

        var blocks = new List<Block>();
        for (var page = from; page <= to; page++)
        {
            var pdfPage = document.GetPage(page);
            var glyphs = PdfPageAdapter.ReadGlyphs(pdfPage);
            var runs = GlyphMerger.Merge(glyphs);
            var lines = LineBuilder.Build(runs);
            blocks.AddRange(BlockClassifier.Classify(page, lines, CodeMarginOf(pdfPage.Width, lines)));
        }

        var sections = SectionAssembler.Assemble(
            Path.GetFileNameWithoutExtension(path), outline.Entries, blocks);

        return new ExtractedDocument(
            new DocumentInfo(
                File: Path.GetFileName(path),
                Sha256: Sha256OfFile(path),
                Title: document.Information.Title,
                Producer: document.Information.Producer,
                Pages: document.NumberOfPages,
                ExtractedAt: DateTimeOffset.UtcNow.ToString("O"),
                ExtractorVersion: Version),
            warnings,
            sections);
    }

    /// <summary>
    /// 代码区的右边距，用于还原 PDF 排版折行。按版面对称推算：页宽 − 代码左缘。
    /// 实测 dotnet-csharp.pdf p760：595.9 − 77.5 = 518.4，而该页观测到的最大右缘是 498.9 ——
    /// 推算值是真实边距的安全上界，宁可漏接一个折行（只是难读），也不错接两行（产出坏代码）。
    /// 页面没有等宽行时返回 MaxValue，即"一律不接回"。
    /// </summary>
    private static double CodeMarginOf(double pageWidth, IReadOnlyList<TextLine> lines)
    {
        var codeLines = lines.Where(l => l.Runs.Count > 0 && l.Runs.All(r => FontFacts.IsMonospace(r.Font)));
        var left = codeLines.Select(l => l.X).DefaultIfEmpty(double.NaN).Min();

        return double.IsNaN(left) ? double.MaxValue : pageWidth - left;
    }

    private static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
