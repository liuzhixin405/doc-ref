using Docref;
using UglyToad.PdfPig;

namespace Docref.Tests;

public class OutlineReaderTests
{
    /// <summary>
    /// PdfPig 自带的 <c>TryGetBookmarks</c> 在这份文档上只返回 43 个条目 —— 它碰到没有
    /// 目标页的纯容器节点（如 "Language version"、"Types"）就丢掉整棵子树。
    /// 1909 页只识别 43 个章节等于平均 44 页一节，完全不可用，所以必须自己走
    /// <c>/Outlines</c> 的 /First → /Next 链。
    ///
    /// 840 与层级分布是对该文档实测核实过的值。
    /// </summary>
    [Fact]
    public void Reads_the_full_outline_tree_including_container_nodes()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.CSharpLanguageReference));

        var outline = OutlineReader.Read(document);

        Assert.Equal(840, outline.Entries.Count);

        var byLevel = outline.Entries
            .GroupBy(e => e.Level)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(new Dictionary<int, int> { [1] = 14, [2] = 132, [3] = 681, [4] = 13 }, byLevel);
    }

    /// <summary>
    /// 大纲条目指向的是命名目标（<c>/Dest = /D4-</c>），页码要靠 Catalog 的 <c>/Dests</c>
    /// 字典再查一次，目标数组第 0 项是页对象的间接引用，需要一张 页对象号 → 页码 的映射。
    /// 该文档实测 1383 个命名目标，全部可解析、零失败。
    /// 解析率是硬指标：解析不出来的目标意味着对应章节丢失定位。
    /// </summary>
    [Fact]
    public void Resolves_every_named_destination_to_a_page()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.CSharpLanguageReference));

        var outline = OutlineReader.Read(document);

        Assert.Equal(1383, outline.Destinations.Count);
        Assert.All(outline.Destinations.Values, d => Assert.InRange(d.Page, 1, document.NumberOfPages));
    }

    /// <summary>
    /// 命名目标数组形如 <c>[pageRef /XYZ x y zoom]</c>，其中 y 是页内垂直位置，必须取出来。
    ///
    /// 章节主锚点（<c>D3-</c>）的 y 恒为页顶 814.88，而子锚点（<c>D3-edit-the-project-file</c>）
    /// 带精确的页内位置（实测 p7 y=605.63）。这个差别是把 7 页一节的大章节切成细粒度
    /// 检索单元的依据，slug 本身就是现成的检索键。丢了 y 就只能按页切，粒度回到原点。
    /// </summary>
    [Fact]
    public void Keeps_the_in_page_position_of_sub_anchors()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.CSharpLanguageReference));

        var outline = OutlineReader.Read(document);

        var subAnchor = outline.Destinations["D3-edit-the-project-file"];
        Assert.Equal(7, subAnchor.Page);
        Assert.Equal(605.63, subAnchor.Y!.Value, precision: 1);
    }

    /// <summary>
    /// 大纲条目只记着目标名（<c>/Dest = /D4-</c>），页码要回填。
    /// 实测该文档有 26 个纯容器节点（如 "Language version"、"Types"）—— 它们既无 /Dest
    /// 也无 /A，页码就该是 null，不能编一个。正是这批节点让 PdfPig 自带 API 丢掉整棵子树。
    /// </summary>
    [Fact]
    public void Fills_entry_pages_from_their_destination_and_leaves_container_nodes_empty()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.CSharpLanguageReference));

        var outline = OutlineReader.Read(document);

        var containers = outline.Entries.Where(e => e.DestName is null).ToList();
        Assert.Equal(26, containers.Count);
        Assert.All(containers, e => Assert.Null(e.Page));

        Assert.All(outline.Entries.Where(e => e.DestName is not null), e => Assert.NotNull(e.Page));
    }

    /// <summary>
    /// 标题是 <see cref="UglyToad.PdfPig.Tokens.HexToken"/>，中文标题在 PDF 里是带 BOM 的
    /// UTF-16BE 字节串。实测 ASP.NET Core 中文文档的首个条目是 "ASP.NET Core文档" —— 中英混排，
    /// 解码错了会变成乱码或问号。
    /// </summary>
    [Fact]
    public void Decodes_chinese_titles()
    {
        using var document = PdfDocument.Open(PdfFixtures.Path(PdfFixtures.AspNetCoreChinese));

        var outline = OutlineReader.Read(document);

        Assert.Equal("ASP.NET Core文档", outline.Entries[0].Title);
        Assert.Contains(outline.Entries, e => e.Title == "开始");
        Assert.Contains(outline.Entries, e => e.Title == "11 中的新增功能");
    }
}
