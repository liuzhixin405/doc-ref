using UglyToad.PdfPig;

namespace Docref;

/// <summary>判断 PDF 有没有可提取的文本层。扫描件只有图片，必须在提取前识别出来。</summary>
public static class TextLayerProbe
{
    /// <summary>抽查的页数。抽到一页有文本就够了，无需扫全文档。</summary>
    private const int SampleSize = 20;

    /// <summary>一页至少要有多少个字符才算有文本。扫描件偶尔带几个水印字符，阈值挡掉它们。</summary>
    private const int MinimumLettersPerPage = 20;

    /// <summary>
    /// 已知限制：只抽查开头若干页。若某文档前 20 页是扫描封面、后面才是文本，会被误判。
    /// 实测的官方文档不存在这种情况。
    /// </summary>
    public static bool HasTextLayer(PdfDocument document)
    {
        var pagesToProbe = Math.Min(SampleSize, document.NumberOfPages);

        for (var page = 1; page <= pagesToProbe; page++)
        {
            if (document.GetPage(page).Letters.Count >= MinimumLettersPerPage)
            {
                return true;
            }
        }

        return false;
    }
}
