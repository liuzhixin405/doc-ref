using System.Text.Json;
using Docref;

namespace Docref.Tests;

public class JsonlWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("docref-pdf-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// 一行一个 section：可流式写、可流式读、可 grep、可选择性加载。
    /// aspnet-core 那份提出来 30~40 MB，单个大 JSON 加载起来很难受。
    /// 溯源信息与警告放在单独的 manifest 里，不重复进每一行。
    /// </summary>
    [Fact]
    public void Writes_a_manifest_and_one_json_line_per_section()
    {
        var document = Doc(
            Section("doc#0001", "First"),
            Section("doc#0002", "Second"));

        JsonlWriter.Write(document, _directory);

        var lines = File.ReadAllLines(Path.Combine(_directory, "sample.sections.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Equal("doc#0001", JsonDocument.Parse(lines[0]).RootElement.GetProperty("id").GetString());
        Assert.Equal("doc#0002", JsonDocument.Parse(lines[1]).RootElement.GetProperty("id").GetString());

        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "sample.manifest.json")));
        Assert.Equal("sample.pdf", manifest.RootElement.GetProperty("source").GetProperty("file").GetString());
    }

    /// <summary>
    /// 中文必须原样输出。System.Text.Json 默认把非 ASCII 转义成 \uXXXX ——
    /// 那样 JSONL 既不可读、也白白膨胀几倍。中文文档占了样本的一大半，这条不能将就。
    /// </summary>
    [Fact]
    public void Writes_chinese_without_escaping()
    {
        var document = Doc(Section("doc#0001", "ASP.NET Core文档"));

        JsonlWriter.Write(document, _directory);

        var raw = File.ReadAllText(Path.Combine(_directory, "sample.sections.jsonl"));
        Assert.Contains("ASP.NET Core文档", raw);
        Assert.DoesNotContain("\\u", raw);
    }

    /// <summary>
    /// 空字段不该出现在输出里。text 块没有 lang / items / style，
    /// 逐行带一串 null 会把 13000 页的输出白白撑大。
    /// </summary>
    [Fact]
    public void Omits_null_fields()
    {
        var document = Doc(Section("doc#0001", "First"));

        JsonlWriter.Write(document, _directory);

        var line = File.ReadAllLines(Path.Combine(_directory, "sample.sections.jsonl"))[0];
        Assert.DoesNotContain("null", line);
    }

    private static ExtractedDocument Doc(params Section[] sections)
        => new(
            new DocumentInfo("sample.pdf", new string('a', 64), "Sample", "Test", 100, "2026-09-10T00:00:00Z", "0.1.0"),
            [],
            sections);

    private static Section Section(string id, string title)
        => new(id, [title], title, 1, [1, 2], ["dynamic"],
            [new Block(Block.KindText, 1, 0, 800, Text: "body")]);
}
