namespace Docref;

/// <summary>
/// 检索词与文本的匹配。大小写不敏感。
///
/// 关键规则：**拉丁字母的检索词按词边界匹配，中文不按。**
/// 实测教训：纯子串匹配下 "EF" 命中了 4135 个 section 全部（藏在 reference / default /
/// before 里），让 "EF Core 迁移" 刷出 408 分并指向完全无关的文档。
/// 而中文没有词分隔符，若也要求词边界，「什么是」在连续中文里永远匹配不上。
/// </summary>
public static class TermMatcher
{
    public static bool Contains(string? text, string term) => Count(text, term) > 0;

    public static int Count(string? text, string term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
        {
            return 0;
        }

        // 首/末字符是字母数字时才检查该侧的词边界。
        // 这样 Span<T>（末字符是 '>'）与 HttpClient.SendAsync（两端都是字母）都能正常命中。
        var checkLeft = char.IsLetterOrDigit(term[0]) && !TextFacts.IsCjk(term[0]);
        var checkRight = char.IsLetterOrDigit(term[^1]) && !TextFacts.IsCjk(term[^1]);

        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var leftOk = !checkLeft || index == 0 || !IsWordChar(text[index - 1]);
            var end = index + term.Length;
            var rightOk = !checkRight || end == text.Length || !IsWordChar(text[end]);

            if (leftOk && rightOk)
            {
                count++;
            }

            index += term.Length;
        }

        return count;
    }

    /// <summary>构成"词内部"的字符。中文不算 —— 中文与拉丁词之间是天然边界。</summary>
    private static bool IsWordChar(char c)
        => (char.IsLetterOrDigit(c) && !TextFacts.IsCjk(c)) || c == '_';
}
