namespace Docref;

/// <summary>源文档的溯源信息。</summary>
/// <param name="Sha256">
/// 源文件内容哈希。文档换版后同一个 section id 可能指向别的内容，靠它能判定输出是否还对得上源文件。
/// </param>
public sealed record DocumentInfo(
    string File,
    string Sha256,
    string? Title,
    string? Producer,
    int Pages,
    string ExtractedAt,
    string ExtractorVersion);

/// <summary>一份 PDF 的提取结果。</summary>
/// <param name="Warnings">
/// 提取时的不确定项。不静默吞掉 —— 提取器认为可疑的地方必须让人看见。
/// </param>
public sealed record ExtractedDocument(
    DocumentInfo Source,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<Section> Sections);
