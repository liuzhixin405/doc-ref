using Docref;
using UglyToad.PdfPig;

namespace Docref.Tests;

public class TextLayerProbeTests
{
    /// <summary>
    /// C#设计模式_中文版.pdf 是 2001 年 Acrobat Distiller 出的纯扫描件：前 40 页零文本、
    /// 全是图片。这种文件必须被识别出来并拒绝，而不是产出一份空壳 JSON 假装成功 ——
    /// 空壳会静默污染知识库。
    /// </summary>
    [Fact]
    public void Detects_a_scanned_pdf_as_having_no_text_layer()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.ScannedBook));

        Assert.False(TextLayerProbe.HasTextLayer(document));
    }

    /// <summary>
    /// 反面：正常的 Learn 导出必须被判定为有文本层，否则会把好文件误拒。
    /// 误拒和误收一样是缺陷。
    /// </summary>
    [Fact]
    public void Detects_a_normal_pdf_as_having_a_text_layer()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.CSharpLanguageReference));

        Assert.True(TextLayerProbe.HasTextLayer(document));
    }
}
