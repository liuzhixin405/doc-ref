using Docref;

namespace Docref.Tests;

public class FontFactsTests
{
    /// <summary>
    /// Microsoft Learn 用 <c>docons</c> 这个图标字体画小图标，字形是私有使用区字符。
    /// 真实输出里它污染成了 "ﾉExpand table"（那个 ﾉ 就是  落到文本里）。
    /// 图标不是文字，必须整体丢弃。
    /// </summary>
    [Fact]
    public void Recognizes_the_learn_icon_font()
    {
        Assert.True(FontFacts.IsIconFont("DAAAAA+docons"));
    }

    /// <summary>反面：正文与代码字体不能被当成图标丢掉，否则内容会整段消失。</summary>
    [Theory]
    [InlineData("AAAAAC+SegoeUI")]
    [InlineData("AAAAAF+Consolas")]
    [InlineData("MicrosoftYaHei")]
    public void Does_not_mistake_content_fonts_for_icons(string font)
    {
        Assert.False(FontFacts.IsIconFont(font));
    }
}
