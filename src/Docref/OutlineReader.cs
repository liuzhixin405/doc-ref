using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Docref;

/// <summary>
/// 读取 PDF 的大纲与命名目标。
///
/// 不用 PdfPig 的 <c>TryGetBookmarks</c>：它遇到没有目标页的纯容器节点就丢掉整棵子树，
/// 在实测文档上只能拿到 43/840 个条目。这里直接走 <c>/Outlines</c> 的 /First → /Next 链。
/// </summary>
public static class OutlineReader
{
    /// <summary>同级兄弟节点数量上限，防止畸形 PDF 的 /Next 链成环导致死循环。</summary>
    private const int MaxSiblings = 100_000;

    public static PdfOutline Read(PdfDocument document)
    {
        // 目标必须先读，大纲条目的页码要靠它回填。
        var destinations = ReadDestinations(document, MapPageNumbers(document));
        var catalog = document.Structure.Catalog.CatalogDictionary;
        var entries = new List<OutlineEntry>();

        if (catalog.TryGet(NameToken.Create("Outlines"), out var outlinesToken)
            && Resolve(document, outlinesToken) is DictionaryToken outlines)
        {
            Walk(document, outlines, level: 1, destinations, entries);
        }

        return new PdfOutline(entries, destinations);
    }

    /// <summary>
    /// 建立 页对象号 → 页码（1 起）的映射。命名目标数组的第 0 项是页对象的间接引用，
    /// 只有这张表才能把它换算成页码。
    /// </summary>
    private static Dictionary<long, int> MapPageNumbers(PdfDocument document)
    {
        var ordered = new List<long?>();
        var catalog = document.Structure.Catalog.CatalogDictionary;

        if (catalog.TryGet(NameToken.Create("Pages"), out var pages))
        {
            CollectPages(document, pages, ordered);
        }

        var map = new Dictionary<long, int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            // 直接对象（非间接引用）的页无法被命名目标引用，但仍要占一个页序号，
            // 否则后面所有页码都会偏移。
            if (ordered[i] is { } objectNumber)
            {
                map[objectNumber] = i + 1;
            }
        }

        return map;
    }

    private static void CollectPages(PdfDocument document, IToken token, List<long?> ordered)
    {
        var objectNumber = token is IndirectReferenceToken reference
            ? reference.Data.ObjectNumber
            : (long?)null;

        if (Resolve(document, token) is not DictionaryToken node)
        {
            return;
        }

        var type = node.TryGet(NameToken.Create("Type"), out var typeToken)
                   && Resolve(document, typeToken) is NameToken name
            ? name.Data
            : string.Empty;

        if (type == "Page")
        {
            ordered.Add(objectNumber);
            return;
        }

        if (node.TryGet(NameToken.Create("Kids"), out var kidsToken)
            && Resolve(document, kidsToken) is ArrayToken kids)
        {
            foreach (var kid in kids.Data)
            {
                CollectPages(document, kid, ordered);
            }
        }
    }

    /// <summary>
    /// 读取 Catalog 的 <c>/Dests</c> 平铺字典。
    /// 已知限制：不支持 PDF 1.2 起的 <c>/Names /Dests</c> 名称树形式；实测的 Learn 导出
    /// 用的都是平铺 <c>/Dests</c>。遇到名称树形式会得到空结果，由调用方记为警告。
    /// </summary>
    private static Dictionary<string, Destination> ReadDestinations(
        PdfDocument document, Dictionary<long, int> pageNumbers)
    {
        var result = new Dictionary<string, Destination>();
        var catalog = document.Structure.Catalog.CatalogDictionary;

        if (!catalog.TryGet(NameToken.Create("Dests"), out var destsToken)
            || Resolve(document, destsToken) is not DictionaryToken dests)
        {
            return result;
        }

        foreach (var pair in dests.Data)
        {
            if (TryResolveDestination(document, pair.Value, pageNumbers, out var destination))
            {
                result[pair.Key] = destination;
            }
        }

        return result;
    }

    private static bool TryResolveDestination(
        PdfDocument document, IToken token, Dictionary<long, int> pageNumbers, out Destination destination)
    {
        destination = default!;
        var value = Resolve(document, token);

        // 目标既可能直接是数组 [pageRef /XYZ x y z]，也可能包在字典的 /D 里。
        var array = value as ArrayToken;
        if (array is null
            && value is DictionaryToken wrapper
            && wrapper.TryGet(NameToken.Create("D"), out var inner))
        {
            array = Resolve(document, inner) as ArrayToken;
        }

        if (array is null || array.Length == 0
            || array.Data[0] is not IndirectReferenceToken pageReference
            || !pageNumbers.TryGetValue(pageReference.Data.ObjectNumber, out var page))
        {
            return false;
        }

        destination = new Destination(page, ReadY(array));
        return true;

        // 目标数组形如 [pageRef /XYZ x y zoom]。/XYZ 的任一坐标都可能是 null（含义是"保持当前"），
        // 所以模式匹配失败就是没有页内位置，不是错误。
        // 只处理 /XYZ：实测的 Learn 导出全部是这一种。其他模式（/FitH 等）等真遇到再加。
        static double? ReadY(ArrayToken array)
            => array.Length >= 4
               && array.Data[1] is NameToken { Data: "XYZ" }
               && array.Data[3] is NumericToken y
                ? y.Data
                : null;
    }

    private static void Walk(
        PdfDocument document,
        DictionaryToken parent,
        int level,
        IReadOnlyDictionary<string, Destination> destinations,
        List<OutlineEntry> entries)
    {
        if (!parent.TryGet(NameToken.Create("First"), out var firstToken))
        {
            return;
        }

        var node = Resolve(document, firstToken) as DictionaryToken;

        for (var seen = 0; node is not null && seen < MaxSiblings; seen++)
        {
            var destinationName = ReadDestinationName(document, node);
            var destination = destinationName is not null && destinations.TryGetValue(destinationName, out var found)
                ? found
                : null;

            entries.Add(new OutlineEntry(
                Title: ReadTitle(document, node),
                Level: level,
                DestName: destinationName,
                Page: destination?.Page,
                Y: destination?.Y));

            Walk(document, node, level + 1, destinations, entries);

            if (!node.TryGet(NameToken.Create("Next"), out var nextToken))
            {
                break;
            }

            node = Resolve(document, nextToken) as DictionaryToken;
        }
    }

    /// <summary>
    /// 标题在实测文档里是 <see cref="HexToken"/>。PdfPig 的 <c>HexToken.Data</c> 已经处理了
    /// UTF-16BE 的 BOM，中文标题可直接使用，不需要自己解码。
    /// </summary>
    private static string ReadTitle(PdfDocument document, DictionaryToken node)
    {
        if (!node.TryGet(NameToken.Create("Title"), out var token))
        {
            return string.Empty;
        }

        return Resolve(document, token) switch
        {
            StringToken s => s.Data,
            HexToken h => h.Data,
            _ => string.Empty,
        };
    }

    /// <summary>目标可能直接放在 /Dest，也可能藏在 /A 动作字典的 /D 里。</summary>
    private static string? ReadDestinationName(PdfDocument document, DictionaryToken node)
    {
        if (node.TryGet(NameToken.Create("Dest"), out var direct))
        {
            return AsName(Resolve(document, direct));
        }

        if (node.TryGet(NameToken.Create("A"), out var action)
            && Resolve(document, action) is DictionaryToken actionDictionary
            && actionDictionary.TryGet(NameToken.Create("D"), out var inner))
        {
            return AsName(Resolve(document, inner));
        }

        return null;

        static string? AsName(IToken token) => token switch
        {
            NameToken n => n.Data,
            StringToken s => s.Data,
            HexToken h => h.Data,
            _ => null,
        };
    }

    private static IToken Resolve(PdfDocument document, IToken token)
    {
        while (token is IndirectReferenceToken reference)
        {
            var target = document.Structure.TokenScanner.Get(reference.Data);
            if (target is null)
            {
                return NullToken.Instance;
            }

            token = target.Data;
        }

        return token;
    }
}
