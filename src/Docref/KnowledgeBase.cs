using System.Text.Json;

namespace Docref;

/// <summary>
/// 知识库里的一条命中。刻意只带元数据与摘要 ——
/// 单个 section 最长 13 万字符（约 3.3 万 token），全文进不了上下文。
/// 要正文用 <see cref="KnowledgeBase.GetById"/> 按需取。
/// </summary>
public sealed record KbHit(
    string Document,
    string Id,
    string Title,
    IReadOnlyList<string> Path,
    int[] Pages,
    string Snippet,
    int Score);

/// <summary>知识库当前覆盖的一份文档。从 manifest 实时读出，不写死在任何地方。</summary>
public sealed record CoverageEntry(string Document, string? Title, int Pages, int Sections);

/// <summary>对提取产出的 JSONL 做精确检索。没有向量、没有模糊匹配 —— 命中或未命中，可判定。</summary>
public static class KnowledgeBase
{
    private const string JsonlSuffix = ".sections.jsonl";
    private const int SnippetLength = 160;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<KbHit> Search(string directory, string query, int limit)
    {
        var tokens = QueryTokenizer.Split(query);
        if (tokens.Count == 0)
        {
            return [];
        }

        var hits = new List<KbHit>();

        foreach (var file in Directory.EnumerateFiles(directory, "*" + JsonlSuffix))
        {
            var document = System.IO.Path.GetFileName(file);
            document = document[..^JsonlSuffix.Length];

            foreach (var line in File.ReadLines(file))
            {
                // 预筛：任一检索词都不出现的行连 JSON 都不解析。
                // 4135 个 section / 55 MB 全量扫，靠这一步把解析次数压到命中数量级。
                if (!tokens.Any(t => TermMatcher.Contains(line, t)))
                {
                    continue;
                }

                var section = JsonSerializer.Deserialize<Section>(line, Options);
                if (section is null)
                {
                    continue;
                }

                var score = ScoreQuery(section, query, tokens);
                if (score <= 0)
                {
                    continue;
                }

                // 摘要取命中最强那个词的上下文，否则多词查询会给出一段无关的摘要。
                var best = tokens.OrderByDescending(t => Score(section, t)).First();

                hits.Add(new KbHit(
                    Document: document,
                    Id: section.Id,
                    Title: section.Title,
                    Path: section.Path,
                    Pages: section.Pages,
                    Snippet: Snippet(section, best),
                    Score: score));
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// 某个检索词在整个知识库里是否出现过。命中即提前退出，所以常见词很快。
    ///
    /// 用途：多词查询时如实报告哪个词一个都没匹配上。
    /// 实测教训：「什么是Mapster」曾返回 3 分的命中，而这 3 分全部来自「什么是」——
    /// 查询里唯一的技术词根本不存在，结果却看起来像有覆盖。
    /// 不猜哪个词更重要，而是把「这个词零命中」这个事实摊开。
    /// </summary>
    public static bool TermExists(string directory, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*" + JsonlSuffix))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (TermMatcher.Contains(line, term))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 按 id 取全文。搜索只给摘要，要正文必须显式再取一次 —— 上下文预算由调用方掌控。
    /// </summary>
    public static Section? GetById(string directory, string id)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*" + JsonlSuffix))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.IndexOf(id, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                var section = JsonSerializer.Deserialize<Section>(line, Options);
                if (section?.Id == id)
                {
                    return section;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 知识库当前覆盖了什么，从 manifest 实时读。
    /// 刻意不缓存、不写死：补了新文档，清单自动跟着变。
    /// </summary>
    public static IReadOnlyList<CoverageEntry> Coverage(string directory)
    {
        var entries = new List<CoverageEntry>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.manifest.json"))
        {
            var manifest = JsonSerializer.Deserialize<ManifestShape>(File.ReadAllText(file), Options);
            if (manifest?.Source is null)
            {
                continue;
            }

            var document = System.IO.Path.GetFileName(file);
            document = document[..^".manifest.json".Length];

            entries.Add(new CoverageEntry(
                Document: document,
                Title: manifest.Source.Title,
                Pages: manifest.Source.Pages,
                Sections: manifest.SectionCount));
        }

        return entries.OrderByDescending(e => e.Sections).ToList();
    }

    private sealed record ManifestShape(DocumentInfo? Source, int SectionCount);

    /// <summary>
    /// 单个检索词时与只按整串匹配完全一致 —— 保持已公开的分数语义（30 / 100 阈值）。
    ///
    /// 多个检索词时：整串短语命中的分数（短语更精确，值得保留）+ 命中最强的那一个词的分数。
    /// 用 max 而不是 sum：像「什么是」这种高频噪声词不该靠出现次数压倒真正的技术词。
    /// </summary>
    private static int ScoreQuery(Section section, string phrase, IReadOnlyList<string> tokens)
        => tokens.Count == 1
            ? Score(section, tokens[0])
            : Score(section, phrase) + tokens.Max(t => Score(section, t));

    /// <summary>
    /// 标题命中 &gt; 符号精确命中 &gt; 路径命中 &gt; 正文提及次数。
    /// 一个泛用词若只在正文出现一次，不该把真正讲这个主题的那一节埋掉。
    /// </summary>
    private static int Score(Section section, string term)
    {
        var score = 0;

        if (TermMatcher.Contains(section.Title, term))
        {
            score += 100;
        }

        if (section.CodeRefs.Any(c => c.Equals(term, StringComparison.OrdinalIgnoreCase)))
        {
            score += 80;
        }

        if (section.Path.Any(p => TermMatcher.Contains(p, term)))
        {
            score += 50;
        }

        foreach (var block in section.Blocks)
        {
            score += TermMatcher.Count(block.Text, term);
        }

        return score;
    }

    /// <summary>取第一处提到关键词的正文片段；标题命中但正文没提时退回首块。</summary>
    private static string Snippet(Section section, string term)
    {
        var block = section.Blocks.FirstOrDefault(b => TermMatcher.Contains(b.Text, term))
                    ?? section.Blocks.FirstOrDefault();

        var text = block?.Text ?? string.Empty;
        text = text.Replace('\n', ' ');

        return text.Length <= SnippetLength ? text : text[..SnippetLength] + "…";
    }
}
