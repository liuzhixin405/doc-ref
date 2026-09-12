using Docref;

namespace Docref.Tests;

public class KbFormatterTests
{
    /// <summary>
    /// 硬性字符上限。单个 section 最长 91624 字符（约 2.3 万 token），
    /// 不设上限的话一次调用就把上下文灌满，工具反而成了负担。
    /// 截断后如何续取由 <see cref="Truncation_says_how_many_blocks_there_are_and_where_to_resume"/> 覆盖。
    /// </summary>
    [Fact]
    public void Caps_the_output_size()
    {
        var section = SectionWith(
            new Block(Block.KindText, 1, 0, 800, Text: new string('x', 60_000)));

        var text = KbFormatter.FormatSection(section, only: null);

        Assert.True(text.Length < 12_000, $"输出 {text.Length} 字符，超出预算");
    }

    /// <summary>
    /// 只要代码时不该把正文一起吐出来 —— 写代码时最常要的就是「官方怎么写的」那段代码，
    /// 周围的解释往往是纯浪费。
    /// </summary>
    [Fact]
    public void Filters_to_code_blocks_only()
    {
        var section = SectionWith(
            new Block(Block.KindText, 1, 0, 800, Text: "这是一段解释性正文"),
            new Block(Block.KindCode, 1, 1, 700, Lang: "C#", Text: "int a = 123;"));

        var text = KbFormatter.FormatSection(section, only: Block.KindCode);

        Assert.DoesNotContain("这是一段解释性正文", text);
        Assert.Contains("int a = 123;", text);
    }

    /// <summary>
    /// 每次取全文都要带上溯源信息（id + 页码），因为 skill 要求产出必须标注来源。
    /// 拿不到页码就没法标注，工具就白做了。
    /// </summary>
    [Fact]
    public void Includes_the_citation_fields()
    {
        var section = SectionWith(new Block(Block.KindText, 42, 0, 800, Text: "正文"));

        var text = KbFormatter.FormatSection(section, only: null);

        Assert.Contains("doc#0001", text);
        Assert.Contains("42", text);
    }

    /// <summary>
    /// 实测 35.2% 的 section 超过预算，所以截断是常态、不是边缘情况。
    /// 必须说清「输出了哪些块 / 共多少块 / 从哪继续」，否则调用方不知道自己缺了什么 ——
    /// 拿着不完整的官方文档去写代码，比不查更危险。
    ///
    /// 旧提示让人「按页码范围分次取」，而 --pages 参数根本不存在。指一条不存在的路
    /// 比不指路更糟。
    /// </summary>
    [Fact]
    public void Truncation_says_how_many_blocks_there_are_and_where_to_resume()
    {
        var section = SectionWith(Enumerable.Range(0, 10)
            .Select(i => new Block(Block.KindText, 1, i, 800 - i, Text: new string('x', 2000)))
            .ToArray());

        var text = KbFormatter.FormatSection(section, only: null);

        Assert.Contains("共 10 块", text);
        Assert.Contains("--from", text);
        Assert.DoesNotContain("--pages", text);
    }

    /// <summary>
    /// 续取游标。序号是 section 内的位置，不是 block.Ord ——
    /// Ord 是页内序号，跨页会重复，当游标会有歧义。
    /// </summary>
    [Fact]
    public void Starts_from_a_given_block_index()
    {
        var section = SectionWith(
            new Block(Block.KindText, 1, 0, 800, Text: "第一块"),
            new Block(Block.KindText, 2, 0, 800, Text: "第二块"),
            new Block(Block.KindText, 2, 1, 700, Text: "第三块"));

        var text = KbFormatter.FormatSection(section, only: null, from: 2);

        Assert.DoesNotContain("第一块", text);
        Assert.DoesNotContain("第二块", text);
        Assert.Contains("第三块", text);
    }

    /// <summary>
    /// 块索引：让调用方先看清这一节有什么，直接跳到需要的块，而不是分十几次翻页。
    /// 660 块的 section 存在（0.9%），所以索引本身也要守预算并给出续取方式。
    /// </summary>
    [Fact]
    public void Outline_lists_blocks_with_index_kind_and_preview()
    {
        var section = SectionWith(
            new Block(Block.KindText, 759, 0, 800, Text: "一段解释性正文，说明这个类型的用途"),
            new Block(Block.KindCode, 760, 1, 700, Lang: "C#",
                Text: "public static async Task<int> RunAsync(CancellationToken token)\n"
                      + "{\n    return await ComputeTheAnswerAsync(token);\n}"));

        var text = KbFormatter.FormatOutline(section);

        Assert.Contains("#0", text);
        Assert.Contains("#1", text);
        Assert.Contains("code", text);
        Assert.Contains("p760", text);
        // 索引只给预览，不给全文 —— 否则它就等于取全文，失去意义
        Assert.DoesNotContain("ComputeTheAnswerAsync", text);
    }

    private static Section SectionWith(params Block[] blocks)
        => new("doc#0001", ["Types", "Value types"], "Value types", 2, [42, 43], ["dynamic"], blocks);
}
