using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Docref;

/// <summary>
/// 把提取结果写成两个文件：
/// <c>&lt;stem&gt;.manifest.json</c>（溯源信息与警告）与
/// <c>&lt;stem&gt;.sections.jsonl</c>（一行一个 section）。
///
/// 用 JSONL 而不是单个大 JSON：aspnet-core 那份提出来 30~40 MB，
/// 一行一个 section 才能流式写、流式读、grep、选择性加载。
/// </summary>
public static class JsonlWriter
{
    /// <summary>
    /// 不转义非 ASCII。名字里的 "Unsafe" 指的是 HTML 注入场景 —— 这里输出的是数据文件，
    /// 不进 HTML。默认编码器会把中文写成 \uXXXX，让文件既不可读又膨胀几倍。
    /// </summary>
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions ManifestOptions = new(LineOptions)
    {
        WriteIndented = true,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(ExtractedDocument document, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var stem = Path.GetFileNameWithoutExtension(document.Source.File);

        // 大纲树不重复写进 manifest —— 它可以从每行的 path 无损重建。
        var manifest = new
        {
            source = document.Source,
            warnings = document.Warnings,
            sectionCount = document.Sections.Count,
            blockCount = document.Sections.Sum(s => s.Blocks.Count),
        };

        File.WriteAllText(
            Path.Combine(outputDirectory, $"{stem}.manifest.json"),
            JsonSerializer.Serialize(manifest, ManifestOptions),
            Utf8NoBom);

        using var writer = new StreamWriter(
            Path.Combine(outputDirectory, $"{stem}.sections.jsonl"), append: false, Utf8NoBom);

        foreach (var section in document.Sections)
        {
            writer.WriteLine(JsonSerializer.Serialize(section, LineOptions));
        }
    }
}
