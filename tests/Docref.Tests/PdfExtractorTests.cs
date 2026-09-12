using Docref;

namespace Docref.Tests;

public class PdfExtractorTests
{
    /// <summary>
    /// 端到端：只提取第 10~12 页（全本 1909 页要 20 秒，测试里不划算）。
    ///
    /// 断言的内容都在探针里核实过 —— 该区间有代码块 <c>int a = 123;</c>
    /// 和行内代码 <c>dynamic</c>。这条测试串起整条链路：
    /// 字符 → run → 行 → 块 → section。
    /// </summary>
    [Fact]
    public void Extracts_sections_with_code_blocks_and_inline_code()
    {
        var document = PdfExtractor.Extract(
            PdfFixtures.Path(PdfFixtures.CSharpLanguageReference),
            firstPage: 10,
            lastPage: 12);

        var withContent = document.Sections.Where(s => s.Blocks.Count > 0).ToList();
        Assert.NotEmpty(withContent);

        var codeBlocks = withContent.SelectMany(s => s.Blocks).Where(b => b.Kind == Block.KindCode).ToList();
        Assert.Contains(codeBlocks, b => b.Text!.Contains("int a = 123;"));

        var textBlocks = withContent.SelectMany(s => s.Blocks).Where(b => b.Kind == Block.KindText).ToList();
        Assert.Contains(textBlocks, b => b.Text!.Contains("`dynamic`"));
    }

    /// <summary>
    /// 页码、总页数、标题这些溯源信息必须记下来 —— 「每条知识都能指回 PDF 第几页」
    /// 是这个知识库区别于 LLM 的关键属性：可验证。
    /// </summary>
    [Fact]
    public void Records_provenance_of_the_source_document()
    {
        var document = PdfExtractor.Extract(
            PdfFixtures.Path(PdfFixtures.CSharpLanguageReference),
            firstPage: 10,
            lastPage: 11);

        Assert.Equal("dotnet-csharp-language-reference.pdf", document.Source.File);
        Assert.Equal(1909, document.Source.Pages);
        Assert.Equal("dotnet csharp language-reference | Microsoft Learn", document.Source.Title);
        Assert.Equal(64, document.Source.Sha256.Length);
    }

    /// <summary>
    /// 图标字体的字形必须在最外层就丢掉。真实输出里这一块曾是 "ﾉExpand table" ——
    /// 开头那个字符是 docons 的图标字形，不是文字。
    /// </summary>
    [Fact]
    public void Drops_icon_font_glyphs()
    {
        var document = PdfExtractor.Extract(
            PdfFixtures.Path(PdfFixtures.CSharpLanguageReference),
            firstPage: 10,
            lastPage: 10);

        var texts = document.Sections.SelectMany(s => s.Blocks).Select(b => b.Text ?? string.Empty).ToList();

        Assert.Contains("Expand table", texts);
    }

    /// <summary>
    /// 扫描件必须直接拒绝，而不是产出空壳 JSON 假装成功 —— 空壳会静默污染知识库。
    /// </summary>
    [Fact]
    public void Refuses_a_scanned_pdf()
    {
        var error = Assert.Throws<NotSupportedException>(
            () => PdfExtractor.Extract(PdfFixtures.Path(PdfFixtures.ScannedBook)));

        Assert.Contains("文本层", error.Message);
    }
}
