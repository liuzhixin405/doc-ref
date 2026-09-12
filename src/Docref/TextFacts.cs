namespace Docref;

/// <summary>字符层面的文本判断。</summary>
public static class TextFacts
{
    /// <summary>
    /// 中日韩文字与全角标点。
    ///
    /// 两处用到它，性质相同：中文与拉丁字母之间存在天然边界。
    /// <see cref="BlockClassifier"/> 用它判断折行处要不要补空格；
    /// <see cref="QueryTokenizer"/> 用它在这个边界上切分查询串。
    /// </summary>
    public static bool IsCjk(char c)
        => c is >= '　' and <= '〿'   // CJK 标点（含 。、）
            or >= '㐀' and <= '䶿'    // 扩展 A
            or >= '一' and <= '鿿'    // 基本区
            or >= '＀' and <= '￯';   // 全角形式
}
