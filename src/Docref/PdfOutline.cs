namespace Docref;

/// <summary>命名目标解析出的位置。</summary>
/// <param name="Y">页内垂直位置（PDF 坐标，Y 向上）。缺失时为 null。</param>
public sealed record Destination(int Page, double? Y);

/// <summary>大纲里的一个条目。</summary>
/// <param name="DestName">该条目指向的命名目标（如 <c>D4-</c>）。纯容器节点没有目标，为 null。</param>
/// <param name="Page">目标页码（1 起）。纯容器节点为 null。</param>
public sealed record OutlineEntry(string Title, int Level, string? DestName, int? Page, double? Y);

/// <summary>文档的大纲，以及全部命名目标。</summary>
/// <param name="Destinations">
/// 键形如 <c>D4-</c>（章节主锚点）或 <c>D4-integer-literals</c>（章节内小标题锚点）。
/// 后者带精确的页内 Y，是把大章节切成细粒度检索单元的依据，slug 本身就是现成的检索键。
/// </param>
public sealed record PdfOutline(
    IReadOnlyList<OutlineEntry> Entries,
    IReadOnlyDictionary<string, Destination> Destinations);
