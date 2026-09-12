using System.Text.RegularExpressions;

namespace Docref;

/// <summary>把大纲条目与页面块组装成 <see cref="Section"/>。</summary>
public static class SectionAssembler
{
    /// <summary>行内代码在块文本里已渲染为反引号，从这里回收符号候选。</summary>
    private static readonly Regex InlineCode = new("`([^`]+)`", RegexOptions.Compiled);

    public static IReadOnlyList<Section> Assemble(
        string documentStem,
        IReadOnlyList<OutlineEntry> entries,
        IReadOnlyList<Block> blocks)
    {
        // 按文档顺序排锚点。Y 缺失时视为页顶，等于「整页都归它」。
        var anchors = CollectAnchors(entries)
            .OrderBy(a => a.Entry.Page!.Value)
            .ThenByDescending(a => a.Entry.Y ?? double.MaxValue)
            .ToList();

        var buckets = AssignBlocks(anchors, blocks);
        var lastPage = blocks.Count == 0 ? 0 : blocks.Max(b => b.Page);

        var sections = new List<Section>(anchors.Count);

        for (var i = 0; i < anchors.Count; i++)
        {
            var (index, entry, path) = anchors[i];
            var startPage = entry.Page!.Value;

            // 大纲只给起始页，结束页要靠下一个锚点推出来。
            var endPage = i + 1 < anchors.Count
                ? Math.Max(startPage, anchors[i + 1].Entry.Page!.Value - 1)
                : Math.Max(startPage, lastPage);

            sections.Add(new Section(
                Id: $"{documentStem}#{index:D4}",
                Path: path,
                Title: entry.Title,
                Level: entry.Level,
                Pages: [startPage, endPage],
                CodeRefs: CollectCodeRefs(buckets[i]),
                Blocks: buckets[i]));
        }

        return sections;
    }

    /// <summary>
    /// 收集行内代码作为符号候选，去重并保持首次出现顺序。
    /// 代码块不参与 —— 它整段都是代码，逐行收进来只会淹掉真正的符号引用。
    /// </summary>
    private static string[] CollectCodeRefs(IEnumerable<Block> blocks)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            if (block.Kind == Block.KindCode || block.Text is null)
            {
                continue;
            }

            foreach (Match match in InlineCode.Matches(block.Text))
            {
                var value = match.Groups[1].Value;
                if (seen.Add(value))
                {
                    ordered.Add(value);
                }
            }
        }

        return [.. ordered];
    }

    /// <summary>
    /// 单遍归并：块与锚点都按文档顺序，指针一起往前推，所以是 O(n+m) 而不是 O(n·m)。
    /// 7780 页的文档有上千个锚点和十万级的块，逐节扫全量块会白烧几亿次比较。
    /// </summary>
    private static List<List<Block>> AssignBlocks(
        List<(int Index, OutlineEntry Entry, string[] Path)> anchors,
        IReadOnlyList<Block> blocks)
    {
        var buckets = anchors.Select(_ => new List<Block>()).ToList();

        var current = -1;
        var next = 0;

        foreach (var block in blocks.OrderBy(b => b.Page).ThenBy(b => b.Ord))
        {
            while (next < anchors.Count && AtOrAfter(block, anchors[next].Entry))
            {
                current = next;
                next++;
            }

            // current < 0 表示这个块位于第一个锚点之前（封面、目录之类），不属于任何 section。
            if (current >= 0)
            {
                buckets[current].Add(block);
            }
        }

        return buckets;
    }

    /// <summary>
    /// 块是否位于锚点处或其之后。PDF 坐标 Y 向上，所以同页内「更靠下」意味着 Top 更小。
    /// 把「页 + 页内 Y」统一成一个顺序判断，跨页与同页边界就用同一套逻辑处理。
    /// </summary>
    private static bool AtOrAfter(Block block, OutlineEntry anchor)
    {
        var anchorPage = anchor.Page!.Value;

        if (block.Page != anchorPage)
        {
            return block.Page > anchorPage;
        }

        return block.Top <= (anchor.Y ?? double.MaxValue);
    }

    /// <summary>
    /// 算出每个条目的祖先链，并挑出有目标页的条目。
    /// 纯容器节点参与构成路径，但自身不产出 section —— 它在 PDF 里没有对应内容。
    /// </summary>
    private static List<(int Index, OutlineEntry Entry, string[] Path)> CollectAnchors(
        IReadOnlyList<OutlineEntry> entries)
    {
        var anchors = new List<(int, OutlineEntry, string[])>();

        // 层级栈：ancestry[n-1] 是第 n 层的标题。
        var ancestry = new List<string>();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            // 退回到父级再压入自己。层级跳跃（如 L1 直接到 L3）时路径会短于 level，
            // 这是可接受的降级 —— 不崩、也不编造中间层。
            while (ancestry.Count >= entry.Level)
            {
                ancestry.RemoveAt(ancestry.Count - 1);
            }

            ancestry.Add(entry.Title);

            if (entry.Page is not null)
            {
                anchors.Add((i, entry, ancestry.ToArray()));
            }
        }

        return anchors;
    }
}
