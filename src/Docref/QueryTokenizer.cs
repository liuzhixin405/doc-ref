using System.Text;

namespace Docref;

/// <summary>
/// 把查询串切成检索词。
///
/// 只在两处切：空白，以及中文与非中文的边界。**刻意不在标点上切** ——
/// <c>HttpClient.SendAsync</c>、<c>Span&lt;T&gt;</c> 这类符号必须保持完整，
/// API 名字差一个字符就是错的。
///
/// 也**不做中文分词**（那需要词典）。所以连续中文保持整块，纯中文提问若整个短语
/// 不在语料里仍会未命中 —— 可接受，因为技术词几乎总是拉丁字母，脚本边界切分
/// 正好把它们分离出来。
/// </summary>
public static class QueryTokenizer
{
    public static IReadOnlyList<string> Split(string query)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool? currentIsCjk = null;

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            current.Clear();
            currentIsCjk = null;
        }

        foreach (var ch in query)
        {
            if (char.IsWhiteSpace(ch))
            {
                Flush();
                continue;
            }

            var isCjk = TextFacts.IsCjk(ch);

            if (currentIsCjk is not null && isCjk != currentIsCjk)
            {
                Flush();
            }

            currentIsCjk = isCjk;
            current.Append(ch);
        }

        Flush();
        return tokens;
    }
}
