using Docref;

namespace Docref.Tests;

public class QueryTokenizerTests
{
    /// <summary>
    /// 真实缺陷：`docref kb 什么是signalr` 报「知识库里没有」，而 `docref kb signalr` 有 126 行命中。
    /// 原因是整条查询串被当成一个字面子串去匹配，而「什么是signalr」在语料里出现 0 次。
    ///
    /// 这是假阴性，是这套东西最严重的失效模式 —— 它会让人理直气壮地宣布
    /// 「库里没有 SignalR」，然后合法地改用模型知识。
    /// </summary>
    [Fact]
    public void Splits_at_the_boundary_between_chinese_and_latin()
    {
        Assert.Equal(["什么是", "signalr"], QueryTokenizer.Split("什么是signalr"));
    }

    /// <summary>符号必须保持完整。API 名字差一个字符就是错的，切开就丧失了精确匹配的意义。</summary>
    [Theory]
    [InlineData("HttpClient.SendAsync")]
    [InlineData("Span<T>")]
    [InlineData("System.Threading.Tasks.ValueTask")]
    [InlineData("IAsyncEnumerable<JToken>")]
    public void Keeps_symbols_intact(string symbol)
    {
        Assert.Equal([symbol], QueryTokenizer.Split(symbol));
    }

    [Fact]
    public void Splits_on_whitespace()
    {
        Assert.Equal(["what", "is", "signalr"], QueryTokenizer.Split("what is signalr"));
    }

    /// <summary>
    /// 空格与脚本边界都切，但**不做中文分词** —— 那需要词典，不在范围内。
    /// 所以连续中文保持整块："中心怎么配置" 不会被切成四个词。
    ///
    /// 后果（已知限制）：纯中文提问若整个短语不在语料里，仍会未命中。
    /// 可接受，因为技术词几乎总是拉丁字母，而脚本边界切分正好把它们分离出来。
    /// </summary>
    [Fact]
    public void Splits_on_script_boundary_but_does_not_segment_chinese_words()
    {
        Assert.Equal(["SignalR", "中心怎么配置"], QueryTokenizer.Split("SignalR 中心怎么配置"));
    }

    [Fact]
    public void Returns_a_single_token_unchanged()
    {
        Assert.Equal(["signalr"], QueryTokenizer.Split("signalr"));
    }
}
