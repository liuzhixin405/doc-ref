using Docref;

namespace Docref.Tests;

public class SectionAssemblerTests
{
    /// <summary>
    /// 大纲只给层级数字，祖先链要自己按层级栈算出来。纯容器节点（无目标页）参与构成路径，
    /// 但自身不产出 section —— 它在 PDF 里没有对应内容。
    /// 真实结构：L1 "Types"（容器）→ L2 "Built-in types" → L3 "Built-in reference types"。
    /// </summary>
    [Fact]
    public void Builds_the_ancestor_path_and_skips_container_nodes()
    {
        var entries = new[]
        {
            Entry("Types", level: 1, page: null),
            Entry("Built-in types", level: 2, page: 10),
            Entry("Built-in reference types", level: 3, page: 10),
        };

        var sections = SectionAssembler.Assemble("doc", entries, blocks: []);

        Assert.Equal(2, sections.Count);
        Assert.Equal(["Types", "Built-in types"], sections[0].Path);
        Assert.Equal(["Types", "Built-in types", "Built-in reference types"], sections[1].Path);
    }

    /// <summary>
    /// 一个 section 的页范围是「从自己的起始页，到下一个锚点的起始页之前」。
    /// 大纲只给起始页，结束页必须靠下一个锚点推出来。
    /// </summary>
    [Fact]
    public void Assigns_blocks_to_the_section_that_spans_their_page()
    {
        var entries = new[]
        {
            Entry("First", level: 1, page: 10),
            Entry("Second", level: 1, page: 12),
        };

        var blocks = new[]
        {
            Blk(page: 10, ord: 0),
            Blk(page: 11, ord: 0),
            Blk(page: 12, ord: 0),
        };

        var sections = SectionAssembler.Assemble("doc", entries, blocks);

        Assert.Equal([10, 11], sections[0].Pages);
        Assert.Equal(2, sections[0].Blocks.Count);

        Assert.Equal([12, 12], sections[1].Pages);
        Assert.Single(sections[1].Blocks);
    }

    /// <summary>
    /// 边界页：一页的上半是前一节的末尾，下半是下一节的开头。大纲只给起始页，
    /// 只按页归属会让这一页的块被两个 section 各拿一份（重复）或全归一个（错位）。
    /// 命名目标带页内 Y（实测子锚点如 D3-edit-the-project-file 在 p7 y=605.63），
    /// 靠它可以把边界页精确劈开。
    /// </summary>
    [Fact]
    public void Splits_a_boundary_page_at_the_next_anchor_position()
    {
        var entries = new[]
        {
            Entry("First", level: 1, page: 7, y: 814.88),
            Entry("Second", level: 1, page: 7, y: 400.0),
        };

        var blocks = new[]
        {
            Blk(page: 7, ord: 0, top: 700.0),
            Blk(page: 7, ord: 1, top: 380.0),
        };

        var sections = SectionAssembler.Assemble("doc", entries, blocks);

        Assert.Equal(["p7#0"], sections[0].Blocks.Select(b => b.Text));
        Assert.Equal(["p7#1"], sections[1].Blocks.Select(b => b.Text));
    }

    /// <summary>
    /// 行内代码在提取阶段已被识别成等宽 run 并渲染为反引号。这里顺手收集成符号候选 ——
    /// 「哪一段是等宽字体」这个信息只有提取器知道，下游拿不到。
    /// 需要去重且保持首次出现顺序，否则同一个符号会刷屏。
    /// </summary>
    [Fact]
    public void Collects_inline_code_as_code_refs()
    {
        var entries = new[] { Entry("S", level: 1, page: 10) };

        var blocks = new[]
        {
            new Block(Block.KindText, 10, 0, 800, Text: "The `dynamic` type is similar to `object`."),
            new Block(Block.KindText, 10, 1, 700, Text: "You can't use `new dynamic()` or `dynamic`."),
        };

        var sections = SectionAssembler.Assemble("doc", entries, blocks);

        Assert.Equal(["dynamic", "object", "new dynamic()"], sections[0].CodeRefs);
    }

    private static Block Blk(int page, int ord, double top = 800.0)
        => new(Block.KindText, page, ord, Top: top, Text: $"p{page}#{ord}");

    private static OutlineEntry Entry(string title, int level, int? page, double? y = 814.88)
        => new(title, level, page is null ? null : $"D{page}-", page, page is null ? null : y);
}
