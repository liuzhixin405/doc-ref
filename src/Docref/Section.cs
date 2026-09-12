namespace Docref;

/// <summary>
/// 输出中的一个 section —— 对应大纲里的一个有目标页的条目。
/// </summary>
/// <param name="Id">
/// 稳定标识，形如 <c>dotnet-csharp-language-reference#0412</c>。
/// 不能用 <paramref name="Path"/> 当标识：文档里有一大堆同名的 "Overview"。
/// </param>
/// <param name="Path">从根到自身的完整祖先链，末项即自身标题。让 section 自描述，
/// 下游不必遍历树就知道它在哪。</param>
/// <param name="Pages">[起始页, 结束页]。</param>
/// <param name="CodeRefs">正文里出现过的行内代码片段。提取时已识别出等宽 run，顺手收集，
/// 下游拿不到这个信息。</param>
public sealed record Section(
    string Id,
    IReadOnlyList<string> Path,
    string Title,
    int Level,
    int[] Pages,
    IReadOnlyList<string> CodeRefs,
    IReadOnlyList<Block> Blocks);
