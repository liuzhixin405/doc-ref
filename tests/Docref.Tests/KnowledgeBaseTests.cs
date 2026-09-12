using System.Text;
using System.Text.Json;
using Docref;

namespace Docref.Tests;

public class KnowledgeBaseTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("docref-kb-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// 命中要按相关度排序：标题命中的排在只在正文提过一次的前面。
    /// 否则一个泛用词会把真正讲这个主题的那一节埋掉。
    /// </summary>
    [Fact]
    public void Ranks_a_title_match_above_a_body_mention()
    {
        WriteJsonl("doc",
            Section("doc#0001", "Value types", body: "无关内容"),
            Section("doc#0002", "Something else", body: "顺带提到 value types 一次"));

        var hits = KnowledgeBase.Search(_directory, "value types", limit: 10);

        Assert.Equal(["doc#0001", "doc#0002"], hits.Select(h => h.Id));
    }

    /// <summary>
    /// 真实缺陷复现：`docref kb 什么是signalr` 报「知识库里没有」，而 `docref kb signalr`
    /// 有 126 行命中。整条查询串被当成一个字面子串匹配，而「什么是signalr」在语料里出现 0 次。
    ///
    /// 假阴性是这套东西最严重的失效模式 —— 它会让人理直气壮地宣布「库里没有 SignalR」，
    /// 然后合法地改用模型知识，而这正是整个知识库要防的事。
    /// </summary>
    [Fact]
    public void Finds_a_section_when_the_query_mixes_a_question_with_a_keyword()
    {
        WriteJsonl("doc", Section("doc#0001", "SignalR 概述", body: "SignalR 是一个实时通信库"));

        var hits = KnowledgeBase.Search(_directory, "什么是signalr", limit: 10);

        Assert.Equal("doc#0001", Assert.Single(hits).Id);
    }

    /// <summary>
    /// 真实缺陷：检索词 "EF" 曾命中全部 4135 个 section —— 因为 "ef" 藏在
    /// reference / default / before 里面。于是 "EF Core 迁移" 刷出 408 分并指向
    /// C# 语言参考，一个看起来权威却完全错误的结果。假阳性比假阴性更危险：
    /// 分数阈值挡不住它。
    /// </summary>
    [Fact]
    public void Does_not_match_a_latin_term_hidden_inside_longer_words()
    {
        WriteJsonl("doc", Section("doc#0001", "Reference", body: "see the reference for defaults before use"));

        Assert.Empty(KnowledgeBase.Search(_directory, "EF", limit: 10));
    }

    /// <summary>
    /// 真实缺陷：`docref kb 什么是Mapster` 返回 3 分的命中，而 `docref kb Mapster` 未命中 ——
    /// 那 3 分全部来自「什么是」。查询里唯一的技术词根本不存在，结果却看起来像有覆盖。
    ///
    /// 修法不是猜哪个词更重要（那需要 df 之类的机制），而是**如实报告哪个词一个都没匹配上**，
    /// 让调用方自己判断。逐词存在性检查，命中即提前退出。
    /// </summary>
    [Fact]
    public void Reports_whether_a_term_exists_anywhere()
    {
        WriteJsonl("doc", Section("doc#0001", "SignalR 概述", body: "SignalR 是实时通信库"));

        Assert.True(KnowledgeBase.TermExists(_directory, "SignalR"));
        Assert.False(KnowledgeBase.TermExists(_directory, "Mapster"));
    }

    /// <summary>
    /// 搜索结果只给元数据 + 摘要，绝不给全文。单个 section 最长 13 万字符，
    /// 一次命中就能把上下文灌满 —— 这条是整个查询工具能不能用的前提。
    /// </summary>
    [Fact]
    public void Never_returns_full_text_in_search_results()
    {
        WriteJsonl("doc", Section("doc#0001", "Value types", body: new string('x', 50_000) + " value types"));

        var hit = Assert.Single(KnowledgeBase.Search(_directory, "value types", limit: 10));

        Assert.True(hit.Snippet.Length < 200, $"摘要长度 {hit.Snippet.Length}，超出预算");
    }

    /// <summary>
    /// 查不到就返回空 —— 这是 skill 契约的基石：「无匹配」是一个明确的答案，
    /// 我据此声明「库里没有」，而不是含糊地给个最相似的东西。
    /// </summary>
    [Fact]
    public void Returns_nothing_for_a_term_that_is_absent()
    {
        WriteJsonl("doc", Section("doc#0001", "Value types", body: "无关内容"));

        Assert.Empty(KnowledgeBase.Search(_directory, "Mapster", limit: 10));
    }

    /// <summary>符号要能精确命中 codeRefs —— API 名字差一点就是错的，不能靠模糊匹配。</summary>
    [Fact]
    public void Matches_a_symbol_exactly_against_code_refs()
    {
        WriteJsonl("doc",
            Section("doc#0001", "无关标题", body: "正文没提", codeRefs: "HttpClient.SendAsync"),
            Section("doc#0002", "另一节", body: "正文没提", codeRefs: "HttpClient"));

        var hits = KnowledgeBase.Search(_directory, "HttpClient.SendAsync", limit: 10);

        Assert.Equal("doc#0001", Assert.Single(hits).Id);
    }

    /// <summary>按 id 取全文。搜索只给摘要，要正文必须显式再取一次 —— 上下文预算由调用方掌控。</summary>
    [Fact]
    public void Fetches_a_full_section_by_id()
    {
        WriteJsonl("doc", Section("doc#0001", "Value types", body: "完整正文在这里"));

        var section = KnowledgeBase.GetById(_directory, "doc#0001");

        Assert.NotNull(section);
        Assert.Equal("完整正文在这里", Assert.Single(section.Blocks).Text);
    }

    /// <summary>
    /// 覆盖面必须从 manifest 实时读出来，不能写死在 skill 里 ——
    /// 补了新文档清单就该自动跟着变，否则 skill 会拿过期清单劝我别去查其实已经有的东西。
    /// </summary>
    [Fact]
    public void Reports_coverage_from_manifests()
    {
        WriteJsonl("doc", Section("doc#0001", "Value types", body: "x"));
        WriteManifest("doc", title: "C# 语言参考", pages: 1909, sections: 814);

        var coverage = Assert.Single(KnowledgeBase.Coverage(_directory));

        Assert.Equal("doc", coverage.Document);
        Assert.Equal("C# 语言参考", coverage.Title);
        Assert.Equal(1909, coverage.Pages);
        Assert.Equal(814, coverage.Sections);
    }

    /// <summary>直接写 manifest JSON，不走 JsonlWriter —— 后者会连带覆盖刚写好的 jsonl。</summary>
    private void WriteManifest(string stem, string title, int pages, int sections)
    {
        var json = $$"""
            {
              "source": {
                "file": "{{stem}}.pdf",
                "sha256": "{{new string('a', 64)}}",
                "title": "{{title}}",
                "producer": "test",
                "pages": {{pages}},
                "extractedAt": "2026-09-10T00:00:00Z",
                "extractorVersion": "0.1.0"
              },
              "warnings": [],
              "sectionCount": {{sections}},
              "blockCount": 0
            }
            """;

        File.WriteAllText(Path.Combine(_directory, $"{stem}.manifest.json"), json, new UTF8Encoding(false));
    }

    private void WriteJsonl(string stem, params Section[] sections)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllLines(
            Path.Combine(_directory, $"{stem}.sections.jsonl"),
            sections.Select(s => JsonSerializer.Serialize(s, options)),
            new UTF8Encoding(false));
    }

    private static Section Section(string id, string title, string body, params string[] codeRefs)
        => new(id, [title], title, 1, [1, 2], codeRefs,
            [new Block(Block.KindText, 1, 0, 800, Text: body)]);
}
