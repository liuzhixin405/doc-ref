namespace Docref;

/// <summary>
/// 输出中的一个内容块。
///
/// 故意用单一 record + <see cref="Kind"/> 字段，而不是多态的 record 继承体系：
/// 它与 JSONL 输出是 1:1 映射，省掉多态序列化的全部麻烦。少一点类型安全，
/// 换掉一整类复杂度。
/// </summary>
/// <param name="Ord">块在本页内的序号。与 <paramref name="Page"/> 一起构成可引用、可回溯的锚点。</param>
/// <param name="Top">块顶端的 Y 坐标（PDF 坐标，Y 向上）。用于把边界页按下一节的起始位置劈开。</param>
public sealed record Block(
    string Kind,
    int Page,
    int Ord,
    double Top = 0,
    string? Text = null,
    IReadOnlyList<string>? Items = null,
    string? Lang = null,
    string? Style = null)
{
    public const string KindText = "text";
    public const string KindCode = "code";
    public const string KindList = "list";
    public const string KindCallout = "callout";
}
