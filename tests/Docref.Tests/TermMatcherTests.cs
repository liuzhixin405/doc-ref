using Docref;

namespace Docref.Tests;

public class TermMatcherTests
{
    /// <summary>
    /// 真实缺陷：检索词 "EF" 在 4135 个 section 里命中 100% —— 因为 "ef" 藏在
    /// reference / default / before 里面。这让 "EF Core 迁移" 刷出 408 分并指向
    /// C# 语言参考，一个看起来很权威却完全错误的结果。
    ///
    /// 拉丁字母的检索词必须按词边界匹配。
    /// </summary>
    [Theory]
    [InlineData("reference")]
    [InlineData("default")]
    [InlineData("before")]
    [InlineData("prefix")]
    public void Does_not_match_a_latin_term_inside_a_longer_word(string text)
    {
        Assert.Equal(0, TermMatcher.Count(text, "EF"));
    }

    /// <summary>反面：作为独立的词必须命中，否则 "EF Core" 这类真实用法就查不到了。</summary>
    [Theory]
    [InlineData("EF Core migrations")]
    [InlineData("使用 EF 做迁移")]
    [InlineData("(EF)")]
    [InlineData("EF")]
    public void Matches_a_latin_term_as_a_standalone_word(string text)
    {
        Assert.Equal(1, TermMatcher.Count(text, "EF"));
    }

    /// <summary>
    /// 中文没有词分隔符，所以中文检索词不能要求词边界 ——
    /// 否则「什么是」在连续中文里永远匹配不上。
    /// </summary>
    [Fact]
    public void Matches_a_chinese_term_inside_continuous_text()
    {
        Assert.Equal(1, TermMatcher.Count("这一节讲的是什么是依赖注入", "什么是"));
    }

    /// <summary>
    /// 带符号的检索词：首字符是字母就检查左边界，末字符不是字母就不检查右边界。
    /// 这样 <c>Span&lt;T&gt;</c> 和 <c>HttpClient.SendAsync</c> 都能正常命中。
    /// </summary>
    [Theory]
    [InlineData("conversions to Span<T> are implicit", "Span<T>")]
    [InlineData("call HttpClient.SendAsync(request)", "HttpClient.SendAsync")]
    public void Matches_terms_that_contain_symbols(string text, string term)
    {
        Assert.Equal(1, TermMatcher.Count(text, term));
    }

    /// <summary>大小写不敏感 —— 文档里 SignalR / signalr / SIGNALR 都出现过。</summary>
    [Fact]
    public void Ignores_case()
    {
        Assert.Equal(2, TermMatcher.Count("SignalR and signalr", "SIGNALR"));
    }

    [Fact]
    public void Contains_agrees_with_count()
    {
        Assert.True(TermMatcher.Contains("EF Core", "EF"));
        Assert.False(TermMatcher.Contains("reference", "EF"));
    }
}
