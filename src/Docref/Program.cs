using System.Diagnostics;
using System.Text;
using Docref;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 1;
}

try
{
    return args[0] switch
    {
        "extract" => RunExtract(args[1..]),
        "kb" => RunKb(args[1..]),
        "debug-lines" => RunDebugLines(args[1..]),
        _ => Unknown(args[0]),
    };
}
catch (FileNotFoundException error)
{
    Console.Error.WriteLine($"找不到文件: {error.Message}");
    return 2;
}
catch (DirectoryNotFoundException error)
{
    Console.Error.WriteLine($"找不到目录: {error.Message}");
    return 2;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"未知命令: {command}");
    PrintUsage();
    return 1;
}

static void PrintUsage() => Console.WriteLine("""
    docref —— 技术文档 PDF 知识库。不联网、不调 AI。

      docref extract <pdf|目录> [-o 目录] [--pages 起-止]
          把 PDF 提取成 JSONL。传目录则批量提取其中所有 PDF。
          -o        输出目录，默认 ./out
          --pages   只提取某个页范围，例如 --pages 10-12（调试用）

      docref kb <关键词> [--limit N] [--dir 目录]
          搜索知识库。只返回元数据 + 摘要，不返回正文。
      docref kb --id <section-id> [--only code|text] [--from N] [--dir 目录]
          取出某个 section 的正文。超出预算会截断，并给出续取游标（--from）。
          --only    只要某类块（code / text）
          --from    从 section 内第 N 块开始（截断提示里会给出该从哪继续）
      docref kb --id <section-id> --outline
          列出该 section 的块索引（序号 / 页码 / 类型 / 预览），
          用来直接跳到需要的块，而不是分十几次翻页。
      docref kb --coverage [--dir 目录]
          列出知识库当前覆盖了哪些文档（从 manifest 实时读）。

          --dir 未给时依次取环境变量 DOCREF_KB、默认 D:\pdf\jsonl
    """);

// ---------- debug-lines（诊断用：看某页的原始行几何）----------

static int RunDebugLines(string[] args)
{
    if (args.Length < 2 || !int.TryParse(args[1], out var pageNumber))
    {
        Console.Error.WriteLine("用法: docref debug-lines <pdf> <页码>");
        return 1;
    }

    using var document = UglyToad.PdfPig.PdfDocument.Open(args[0]);
    var page = document.GetPage(pageNumber);

    Console.WriteLine($"页 {pageNumber}  宽 {page.Width:F1}  高 {page.Height:F1}");
    Console.WriteLine($"{"Y",8} {"X左",7} {"X右",7} {"字号",5} {"等宽",4}  文本");

    var lines = LineBuilder.Build(GlyphMerger.Merge(PdfPageAdapter.ReadGlyphs(page)));

    foreach (var line in lines)
    {
        var right = line.Runs.Max(r => r.X + r.Width);
        var size = line.Runs[0].Size;
        var mono = line.Runs.All(r => FontFacts.IsMonospace(r.Font)) ? "是" : "否";
        var text = line.Text.Length > 62 ? line.Text[..62] : line.Text;

        Console.WriteLine($"{line.Y,8:F1} {line.X,7:F1} {right,7:F1} {size,5:F1} {mono,4}  {text}");
    }

    return 0;
}

// ---------- extract ----------

static int RunExtract(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    var input = args[0];
    var output = "out";
    var firstPage = 1;
    int? lastPage = null;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o" or "--out" when i + 1 < args.Length:
                output = args[++i];
                break;

            case "--pages" when i + 1 < args.Length:
                var parts = args[++i].Split('-', 2);
                if (!int.TryParse(parts[0], out firstPage))
                {
                    PrintUsage();
                    return 1;
                }

                lastPage = parts.Length == 2 && int.TryParse(parts[1], out var to) ? to : firstPage;
                break;

            default:
                PrintUsage();
                return 1;
        }
    }

    var inputs = Directory.Exists(input)
        ? Directory.EnumerateFiles(input, "*.pdf").OrderBy(p => new FileInfo(p).Length).ToArray()
        : [input];

    if (inputs.Length == 0)
    {
        Console.Error.WriteLine($"{input} 里没有 PDF。");
        return 2;
    }

    var failed = 0;

    foreach (var pdf in inputs)
    {
        Console.WriteLine($"提取 {Path.GetFileName(pdf)} …");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var document = PdfExtractor.Extract(pdf, firstPage, lastPage);
            JsonlWriter.Write(document, output);

            var blocks = document.Sections.Sum(s => s.Blocks.Count);
            Console.WriteLine($"  {document.Source.Pages} 页 → {document.Sections.Count} section / "
                              + $"{blocks} block，{stopwatch.Elapsed.TotalSeconds:F1}s");

            foreach (var warning in document.Warnings)
            {
                Console.WriteLine($"  警告  {warning}");
            }
        }
        catch (NotSupportedException error)
        {
            Console.WriteLine($"  跳过: {error.Message}");
            failed++;
        }
    }

    Console.WriteLine($"输出 {Path.GetFullPath(output)}");
    return failed == inputs.Length ? 2 : 0;
}

// ---------- kb ----------

static int RunKb(string[] args)
{
    var directory = Environment.GetEnvironmentVariable("DOCREF_KB") ?? @"D:\pdf\jsonl";
    string? term = null;
    string? id = null;
    string? only = null;
    var coverage = false;
    var outline = false;
    var from = 0;
    var limit = 8;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--dir" when i + 1 < args.Length:
                directory = args[++i];
                break;

            case "--id" when i + 1 < args.Length:
                id = args[++i];
                break;

            case "--only" when i + 1 < args.Length:
                only = args[++i];
                break;

            case "--limit" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed):
                limit = parsed;
                i++;
                break;

            case "--from" when i + 1 < args.Length && int.TryParse(args[i + 1], out var start):
                from = Math.Max(0, start);
                i++;
                break;

            case "--outline":
                outline = true;
                break;

            case "--coverage":
                coverage = true;
                break;

            default:
                if (args[i].StartsWith('-'))
                {
                    PrintUsage();
                    return 1;
                }

                term = term is null ? args[i] : term + " " + args[i];
                break;
        }
    }

    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"知识库目录不存在: {directory}（用 --dir 指定，或设置 DOCREF_KB）");
        return 2;
    }

    if (coverage)
    {
        var entries = KnowledgeBase.Coverage(directory);
        if (entries.Count == 0)
        {
            Console.WriteLine($"{directory} 里没有 manifest —— 知识库是空的。");
            return 0;
        }

        Console.WriteLine($"知识库 {directory}：{entries.Count} 份文档，"
                          + $"{entries.Sum(e => e.Sections)} 个 section");
        foreach (var entry in entries)
        {
            Console.WriteLine($"  {entry.Sections,5} section  {entry.Pages,5} 页  {entry.Document}");
            if (!string.IsNullOrWhiteSpace(entry.Title))
            {
                Console.WriteLine($"                            {entry.Title}");
            }
        }

        return 0;
    }

    if (id is not null)
    {
        var section = KnowledgeBase.GetById(directory, id);
        if (section is null)
        {
            Console.WriteLine($"没有 id 为 {id} 的 section。");
            return 3;
        }

        Console.WriteLine(outline
            ? KbFormatter.FormatOutline(section)
            : KbFormatter.FormatSection(section, only, from));
        return 0;
    }

    if (string.IsNullOrWhiteSpace(term))
    {
        PrintUsage();
        return 1;
    }

    var hits = KnowledgeBase.Search(directory, term, limit);

    // 把实际用的检索词摊开给调用方看 —— 不然「什么是signalr」为什么能命中 SignalR、
    // 而纯中文提问为什么不行，都是黑箱。
    // 多词查询还要标出哪个词一个都没匹配上：「什么是Mapster」曾靠「什么是」凑出命中，
    // 让人误以为库里有 Mapster 的内容。
    var tokens = QueryTokenizer.Split(term);
    var tokenNote = string.Empty;
    var missing = new List<string>();

    if (tokens.Count > 1)
    {
        var marks = new List<string>();

        foreach (var token in tokens)
        {
            var exists = KnowledgeBase.TermExists(directory, token);
            if (!exists)
            {
                missing.Add(token);
            }

            marks.Add(exists ? $"{token} ✓" : $"{token} ✗");
        }

        tokenNote = $"（检索词: {string.Join(" | ", marks)}）";
    }

    // "无匹配" 是一个明确的答案，不是失败。调用方据此声明「库里没有」，
    // 而不是含糊地拿一个最相似的东西凑。退出码 3 让脚本也能判定。
    if (hits.Count == 0)
    {
        Console.WriteLine($"知识库里没有「{term}」{tokenNote}。");
        return 3;
    }

    Console.WriteLine($"「{term}」{tokenNote} 命中 {hits.Count} 条：");
    foreach (var hit in hits)
    {
        Console.WriteLine();
        Console.WriteLine($"  [{hit.Score,4}] {hit.Id}   p{hit.Pages[0]}-{hit.Pages[^1]}");
        Console.WriteLine($"         {string.Join(" / ", hit.Path)}");
        Console.WriteLine($"         {hit.Snippet}");
    }

    Console.WriteLine();

    if (missing.Count > 0)
    {
        Console.WriteLine($"注意: {string.Join("、", missing)} 在知识库里零命中 —— "
                          + "上面的结果来自其他检索词，不代表库里有这些内容。");
    }

    Console.WriteLine("取正文: docref kb --id <上面的 id> [--only code]");
    return 0;
}
