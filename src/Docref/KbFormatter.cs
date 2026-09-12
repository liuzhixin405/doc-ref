using System.Text;

namespace Docref;

/// <summary>
/// 把查询结果渲染成给人和给 agent 看的文本，并守住上下文预算。
///
/// 实测 35.2% 的 section 超过单次预算（最大 91624 字符 / 660 块），所以截断是常态。
/// 因此三件事必须同时成立：**每一块都可达**（<c>--from</c> 续取）、
/// **能先看清有什么**（<c>FormatOutline</c> 块索引）、**截断时说清缺了什么**。
/// 拿着不完整的官方文档去写代码，比不查更危险。
/// </summary>
public static class KbFormatter
{
    /// <summary>单次输出的字符上限。单个 section 最长 91624 字符（约 2.3 万 token）。</summary>
    private const int MaxChars = 8_000;

    /// <summary>块索引里每块的预览长度。够看出这块讲什么，又不至于等于取全文。</summary>
    private const int PreviewChars = 40;

    /// <summary>
    /// 块索引：先看清这一节有什么，直接跳到需要的块，而不是分十几次翻页。
    /// </summary>
    public static string FormatOutline(Section section)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, section);
        builder.AppendLine($"块索引（共 {section.Blocks.Count} 块）:");

        for (var i = 0; i < section.Blocks.Count; i++)
        {
            var block = section.Blocks[i];
            var lang = block.Lang is null ? string.Empty : block.Lang + " ";
            var line = $"  #{i,-4} p{block.Page,-5} {block.Kind,-7} {lang}{Preview(block)}";

            if (builder.Length + line.Length > MaxChars)
            {
                builder.AppendLine($"… 索引本身超出预算，已列到 #{i - 1}（共 {section.Blocks.Count} 块）。");
                break;
            }

            builder.AppendLine(line);
        }

        builder.AppendLine();
        builder.AppendLine($"取其中几块: --id {section.Id} --from <#序号>      只取代码: --only code");
        return builder.ToString();
    }

    /// <param name="from">
    /// 从 section 内第几块开始。序号是 section 内的绝对位置，不是 <see cref="Block.Ord"/> ——
    /// Ord 是页内序号，跨页会重复，当游标有歧义。与 <paramref name="only"/> 组合时也按绝对位置
    /// 跳过，这样序号的含义不随过滤条件变化。
    /// </param>
    public static string FormatSection(Section section, string? only, int from = 0)
    {
        var builder = new StringBuilder();
        AppendHeader(builder, section);

        var selected = section.Blocks
            .Select((Block, Index) => (Block, Index))
            .Where(x => x.Index >= from)
            .Where(x => only is null || x.Block.Kind == only)
            .ToList();

        if (selected.Count == 0)
        {
            builder.AppendLine(only is null
                ? $"从 #{from} 起没有内容（共 {section.Blocks.Count} 块）。"
                : $"从 #{from} 起没有 {only} 类型的块（共 {section.Blocks.Count} 块）。");
            return builder.ToString();
        }

        var lastIndex = -1;
        var resumeAt = -1;

        foreach (var (block, index) in selected)
        {
            var rendered = Render(index, block);

            if (builder.Length + rendered.Length > MaxChars)
            {
                resumeAt = index;
                break;
            }

            builder.Append(rendered);
            lastIndex = index;
        }

        if (resumeAt >= 0)
        {
            builder.AppendLine();
            builder.AppendLine($"… 已输出 #{from}-#{lastIndex}，共 {section.Blocks.Count} 块，还有内容未输出。");
            builder.AppendLine($"继续: --id {section.Id} --from {resumeAt}");
            builder.AppendLine($"或先看清结构: --id {section.Id} --outline");
        }

        return builder.ToString();
    }

    /// <summary>溯源信息必须在最前面 —— skill 要求产出标注 [来源: id p页码]，拿不到就没法标。</summary>
    private static void AppendHeader(StringBuilder builder, Section section)
    {
        builder.AppendLine($"id     : {section.Id}");
        builder.AppendLine($"path   : {string.Join(" / ", section.Path)}");
        builder.AppendLine($"pages  : {section.Pages[0]}-{section.Pages[^1]}");

        if (section.CodeRefs.Count > 0)
        {
            builder.AppendLine($"symbols: {string.Join(", ", section.CodeRefs.Take(20))}");
        }

        builder.AppendLine();
    }

    private static string Preview(Block block)
    {
        var text = block.Kind == Block.KindList
            ? string.Join(" / ", block.Items ?? [])
            : block.Text ?? string.Empty;

        text = text.Replace('\n', ' ');
        return text.Length <= PreviewChars ? text : text[..PreviewChars] + "…";
    }

    private static string Render(int index, Block block) => block.Kind switch
    {
        Block.KindCode =>
            $"#{index} [p{block.Page} code{(block.Lang is null ? "" : " " + block.Lang)}]\n{block.Text}\n\n",
        Block.KindList =>
            $"#{index} [p{block.Page} list]\n"
            + string.Join("\n", (block.Items ?? []).Select(i => "- " + i)) + "\n\n",
        _ =>
            $"#{index} [p{block.Page}] {block.Text}\n\n",
    };
}
