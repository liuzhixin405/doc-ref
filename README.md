# docref

把技术文档 PDF 提取成 JSONL 知识库，并对它做精确检索。**不联网、不调 AI，全程确定性。**

```
docref extract <pdf|目录> [-o 目录] [--pages 起-止]     # PDF → JSONL（传目录则批量）
docref kb <关键词> [--limit N]                          # 搜索，只返元数据 + 摘要
docref kb --id <section-id> [--only code] [--from N]    # 取正文，超预算给续取游标
docref kb --id <section-id> --outline                   # 块索引，用于跳到需要的块
docref kb --coverage                                    # 当前覆盖了哪些文档
```

知识库目录：`--dir` > 环境变量 `DOCREF_KB` > 默认 `D:\pdf\jsonl`。
**skill 里一律用 `--dir` 显式指定**，不依赖环境变量 —— 没设的时候会静默回落到那个默认值，
症状是「查不到所以没查」，不会有人发现。

工具是个 .NET 8 控制台程序，放哪都行，但**必须整个目录一起放** ——
单个 `docref.exe` 跑不起来，它只是 apphost，会去找同目录的 `docref.dll`。
只拷 exe 的症状：`The application to execute does not exist: '...\docref.dll'`（实测过）。
把那个目录加进 PATH 后可直接用 `docref`，下文都这么写；**skill 里则一律写全路径**，因为智能体不能假定 PATH 配好了。

---

# 怎么用

## ① 自动 —— 主要用法，你什么都不用做

配套 skill 一份正文、多个落地位置，由 `python dotnet-kb/sync-skills.py` 同步。
正文里机器相关的路径写成占位符（`@DOCREF@` / `@KB@` / `@SYNCWEB@`），同步时才填成实际路径 ——
所以仓库里这份正文推到别的机器也能用，换机器只需改脚本顶部那两行配置（或设同名环境变量）。
知识库默认就在仓库的 `kb/` 目录（相对脚本位置算），clone 下来就有，不用另配。

```bash
python dotnet-kb/sync-skills.py          # 只装 Claude Code（默认）
python dotnet-kb/sync-skills.py --cove   # 额外装一份给 cove
```

| 位置 | 给谁 | 什么时候写 |
|---|---|---|
| `~/.claude/skills/dotnet-kb/` | Claude Code | 默认 |
| `~/.cove/plugins/docref/skills/dotnet-kb-cove/` | cove（和 `docref.exe` 同在一个插件包里）| 加 `--cove` |

默认不写 cove 那份，免得在没有 cove 的机器上凭空造出 `~/.cove/plugins/docref/` 目录。

脚本在写之前会检查 docref 和知识库目录是不是真的在（连 `docref.dll` 有没有一起拷都查），
缺了就直接失败、什么都不写 —— 不猜，也不给你一个「装好了但每条命令都跑空」的结果。

cove 那份的名字必须不同：cove 先加载 `~/.cove/skills`、后加载 `~/.claude/skills`，同名会被后者覆盖。
写任何 C#/.NET 代码前自动生效。
你正常提需求（「给 xxx 加个接口」「改一下这个 Service」），会看到它：

```
列出要用到的 API  →  逐个查库  →  命中就按官方写法写，标注 [来源: id p页码]
                                 →  未命中则逐级降级：
                                      L1 联网查官方域   标 [联网-官方: URL]
                                      L2 联网放开       标 [联网-非官方: URL]
                                      L3 都没有         显式声明「未经任何文档核实」
```

**你该检查的是级别。** `[来源:]` 能翻回 PDF 某页某块，可以少看；
`[联网-*]` 只能指到一个 URL、页面会变，引用的代码必须过编译；L3 要重点审 ——
这套东西的全部价值就是**把「哪几行需要你盯」明确指出来**。

一次实录（任务：写一段带超时的 HttpClient 调用）：

```csharp
// [来源: aspnet-core-aspnetcore-10.0#0247 p1642] AddHttpClient 命名客户端的官方写法
builder.Services.AddHttpClient("GitHub", httpClient =>
{
    httpClient.BaseAddress = new Uri("https://api.github.com/");
    httpClient.Timeout = TimeSpan.FromSeconds(10);          // ← 见声明 ①
});

// [来源: dotnet-architecture-microservices#0065 p432-440] IHttpClientFactory 用于弹性 HTTP 请求
public sealed class GitHubRepos(IHttpClientFactory factory)
{
    public Task<string> GetAsync(string repo, CancellationToken ct)
        => factory.CreateClient("GitHub").GetStringAsync($"repos/{repo}", ct);
}
```

> **① 库里没有讲 `HttpClient.Timeout` 语义的章节。** 2026-09-11 在 cove 上复跑这个任务时，
> 它查了 16 次库，识破命中全是 `CS0200` / `Property declarations` 这类编译器消息的误匹配
> ——技术词标着 `✓` 但那几节都在讲别的，于是判 L0 未命中、降级到 L1，
> 标成 `[联网-官方: learn.microsoft.com/.../httpclient.timeout]`。
> 旧版 skill 在这里只能声明「未经核实」，四级阶梯把它救回了官方依据。

过程中还纠了一次检索词：`HttpClient.SendAsync` 只有 **2 分**（按下面的规则等于没有），
换成 `AddHttpClient` 才拿到 99 分和真正的官方代码。

## ② 自己查

```powershell
docref kb "IHttpClientFactory"                              # 搜索：元数据 + 摘要
docref kb --id aspnet-core-aspnetcore-10.0#0247 --outline    # 这一节有哪些块
docref kb --id aspnet-core-aspnetcore-10.0#0247 --from 22    # 取第 22 块起的正文
docref kb --id aspnet-core-aspnetcore-10.0#0247 --only code  # 只要代码块
docref kb --coverage                                        # 覆盖了哪些文档
```

**提问务必带英文技术词**（不做中文分词）。看两个信号，任一为真就等于库里没有：

| 信号 | 含义 |
|---|---|
| 分数 < 30 | 只是顺带提了几次，那一节在讲别的 |
| 技术词标 `✗` | 该词零命中，结果来自查询里的其他词 |

实测对比（2026-09-11 复测）：`HttpClient` 157 分（整节在讲它）、`FluentValidation` 4 分（只是出现过这个词）。
**拿一句顺带提及当官方依据比不查更糟** —— 它会让人误以为这段经过核实。

## ③ 补文档

两条路径，都落在仓库的 `kb/` 目录：

**官方 PDF** —— `docref extract` 生成，页码能翻回 PDF 核对：

```powershell
docref extract "D:\pdf" -o kb    # 批量；扫描件会被明确拒绝并跳过
```

**没有官方 PDF 的知识**（GitHub wiki / HTML 文档 / 各库自己的文档站）——
联网查到后手工沉淀，产出同样的 JSONL 但标成 `web-sync`：

```bash
python dotnet-kb/sync-web-kb.py <输入.json>
```

覆盖缺口用 `docref kb --coverage` 和逐词查分数来找，**不要凭印象猜**。
一次快照（2026-09-10，6 份文档 / 3210 section）：缺口是 Mapster 0 分、SonarQube 0、
PostgreSQL 4、FluentValidation 4、xUnit 9、Dapper 17、MediatR 26；而 EF Core（330）、
JWT Bearer（335）、minimal API（319）都有实质内容 —— 和事前估计相反，所以才要量。

> 这是快照，不是清单。补了文档就会变，**每次自己查**。
> 同理 skill 里刻意不写任何覆盖清单：写死的清单会过期，还会劝你别去查一个其实已经有的东西。

> 上面七个缺口**都没有官方 PDF**，不能靠 `extract` 自动摄取。
> `sync-web-kb.py` 走的是「联网查到 → 手工整理成 JSON → 落盘」这条慢路径，一条一条累计
> （比如已沉淀 Microsoft.Data.Sqlite 的 CRUD 用法）。

## 知识库随仓库分发（kb/）

仓库根目录的 `kb/` 就是知识库本体：`<名字>.sections.jsonl` + `<名字>.manifest.json` 成对。
`sync-skills.py` 把 skill 的 `--dir` 默认指向它（相对脚本位置 `../kb` 算），所以：

- **换电脑 / 给别人用**：clone 仓库 → 装好 docref → 跑一次 `python dotnet-kb/sync-skills.py`，即可用。
- **知识库持续累计**：`extract` 与 `sync-web-kb.py` 都写进 `kb/`，commit + push 就分发出去了。

两类文档，引用时别混标：

| 类型 | `manifest.source.producer` | 页码 | 引用标注 |
|---|---|---|---|
| PDF 提取 | `Microsoft Learn PDF …` | 真实 PDF 页码 | `[来源: <id> p<页码>]` |
| 联网同步 | `web-sync` | 合成虚拟页码 | `[联网-官方: URL]` / `[联网-非官方: URL]` |

详见 `kb/README.md`。

## 诊断

```powershell
docref debug-lines "D:\pdf\dotnet-csharp.pdf" 760    # 某页的原始行几何（Y / 左右缘 / 字号 / 是否等宽）
```

提取结果不对时先看这个 —— 本项目每一处折行、粘连、空格问题都是靠它定位的，
凭直觉判断根因的那几次全错了。

---

# 参考

`extract` 产出两个文件：

| 文件 | 内容 |
|---|---|
| `<名字>.manifest.json` | 溯源信息（含源文件 sha256）、提取警告、统计 |
| `<名字>.sections.jsonl` | 一行一个 section |

用 JSONL 而不是单个大 JSON：7780 页的文档提出来 15 MB，一行一个 section 才能流式写、流式读、grep、选择性加载。

## 输出形态

```jsonc
{
  "id": "dotnet-csharp-language-reference#0005",       // 稳定标识（path 会重名，有一堆 "Overview"）
  "path": ["Types", "Built-in types"],                 // 完整祖先链，末项即自身标题
  "title": "Built-in types",
  "level": 2,
  "pages": [10, 12],
  "codeRefs": ["dynamic", "object", "Span<T>"],        // 行内代码回收成的符号候选
  "blocks": [
    { "kind": "text", "page": 10, "ord": 1, "top": 763.13,
      "text": "The `dynamic` type is similar to `object`." },
    { "kind": "code", "page": 11, "ord": 6, "top": 470.6,
      "text": "int a = 123;\nSystem.Int32 b = 123;" }
  ]
}
```

`page` + `ord` 是可引用、可回溯的锚点 —— **每条知识都能指回 PDF 第几页第几块**。这是这个知识库区别于 LLM 的关键属性：可验证。

> 这只对 `extract` 出的 PDF 文档成立。`web-sync` 文档的页码是合成的，其「可回溯」落在 manifest 里的来源 URL 上，引用时标 `[联网-*]`。

`top` 是块顶端的 Y 坐标（PDF 坐标，Y 向上），用于把边界页按下一节的起始位置劈开。

## 为什么不做的事

| 不做 | 原因 |
|---|---|
| OCR | 扫描件直接拒绝并报错，不返回空壳 JSON 假装成功 —— 空壳会静默污染知识库 |
| 向量化 / 检索 | 先有干净的 JSON，再谈怎么查 |
| 语义切分（"一个 API 一条"） | 各语言文档结构差异太大，必须按文档定制；而保真的文档树是它的必要输入 |
| 在提取器里决定检索粒度 | **最佳粒度取决于检索方式，而检索方式还没定。提前切是不可逆的** —— 切碎了拼不回来。保留 section + block 锚点则是可逆的 |

## 怎么做到的（三个确定性信号）

1. **书签 / 大纲** —— 官方文档 PDF 自带完整目录树，这是免费且精确的章节结构。
   ⚠️ **不用 PdfPig 的 `TryGetBookmarks`**：它遇到没有目标页的纯容器节点就丢掉整棵子树，
   实测 1909 页的文档只能拿到 43/840 个条目。本项目自己走 `/Outlines` 的 `/First → /Next` 链。
2. **等宽字体** —— 技术文档的代码几乎总是 Consolas，这是识别代码最可靠的信号。
   判据是「整行 100% 等宽」，而不是字号（行内代码 10.2 与代码块 10.5 只差 0.3，太脆）。
3. **命名目标的页内 Y** —— `/Dests` 里 `D3-` 是章节主锚点，`D3-edit-the-project-file` 是章节内
   小标题锚点，带精确页内位置。它既让边界页能精确劈开，slug 本身也是现成的检索键。

## 实测

| 文档 | 页数 | section | block | 耗时 | 输出 |
|---|---|---|---|---|---|
| dotnet-csharp-language-reference | 1909 | 814 | 15545 | 4.5s | — |
| aspnet-core-aspnetcore-11.0（中文） | 7780 | 947 | 83529 | 37s | 15 MB |
| C#设计模式_中文版 | 270 | — | — | — | **拒绝**（纯扫描件，无文本层） |

## PDF 排版折行的还原

长代码行被 PDF 排版折断后，每个视觉行原本都被当成一行代码，取出的代码无法编译。
现在用**贪心换行的逆运算**还原：上一行右缘 + 本行宽度 > 右边距 → 本行是折下来的续行。

三个判据都来自实测，两个我曾凭直觉判断错的方向也记在这里：

- 右边距按版面对称推算（页宽 − 代码左缘），**不用观测到的最大右缘** ——
  某页只有几行短代码时观测值会远小于真实边距，导致相邻两行被粘在一起。
- 「续行顶到容器左缘」**不成立**：实测全部代码行 X 相同，缩进在文本里是真空格。
- 「上一行顶到右边距」**不成立**：`var client = new ` 右缘只有 296.9（边距 518.4），
  它折行是因为下一段太宽放不下。

四道护栏，每一道都由一个真实假阳性驱动：有前导缩进的行不是续行；上一行以 `;` `{` `}`
结尾时不是；以 `//` `/*` `#` 开头的不是（曾把连续注释粘成一行）；
一个块里若超过 40% 的断行都像折行，说明它是每行接近满宽的**数据/控制台输出**
而非折行代码（Northwind CSV 样本曾被粘成一行 3184 字符），整块都不接回。

实测效果：29.9 万行代码里超过 300 字符的从 197 行降到 21 行，最长 3184 → 404 字符。

## 已知限制

- **表头行的列可能粘连**：`"C# type keyword.NET type"`。数据行已修好（`bool System.Boolean`），
  但表头列间隙小于一个整字宽。彻底解决需要真正的表格识别。
- **折行还原是启发式的**，仍有约 0.007% 的代码行偏长（最长 404 字符）。
  引用代码要过编译器 —— 这本来就该做。
- **表格未识别成结构**：每行是一个独立 text 块，不是 `table` 块。
- **部分行内代码识别不到**：`System.Span<T>`、`void` 在 PDF 里用的是 SegoeUI 而非 Consolas
  （很可能是超链接）。宁可漏标，不瞎标。
- **提示框未识别**：`NOTE` / `TIP` / `WARNING` 目前是普通 text 块（图标字形已正确丢弃）。
- **代码块的语言标签未关联**：`"C#"` 仍是独立 text 块，没变成代码块的 `lang`。
- **只支持平铺 `/Dests`**：不支持 PDF 1.2 起的 `/Names /Dests` 名称树。遇到会在 manifest 里报警告。
- **命名目标只处理 `/XYZ` 模式**：`/FitH` 等其他模式的页内位置会缺失（实测的 Learn 导出全是 `/XYZ`）。
- **扫描件检测只抽查开头 20 页**：若某文档前 20 页是扫描封面、后面才是文本，会被误判。

## 检索的三条规则（决定该怎么提问）

1. **拉丁词按词边界匹配。** `EF` 不会命中 `reference` 里的 `ef`。
   实测教训：纯子串匹配下 `EF` 命中全部 3210 个 section，让 `EF Core 迁移` 刷出 408 分并指向完全无关的文档。
2. **不做中文分词**（需要词典）。查询按空白和中英边界切分，所以 `什么是signalr` 有效，
   但连续中文保持整块 —— **提问务必带上英文技术词**。
3. **多词查询会标出零命中的词。** `什么是Mapster` 显示 `Mapster ✗` 并警告结果来自其他检索词，
   因为「有命中」不等于「库里有这个东西」。

## 截断

单次输出上限 8000 字符，**实测 35% 的 section 会超**（最大 397 块 / 9 万字符）。
截断时给出续取游标（`--from N`）与结构提示（`--outline`），所以每一块都可达。
`--from` 的序号是 section 内绝对位置，与 `--only` 无关。

## 开发

```bash
dotnet test          # 89 个测试
dotnet build
```

测试需要真实样本 PDF。默认从 `D:\pdf` 找，可用环境变量 `DOCREF_PDF_FIXTURES` 覆盖。
**样本缺失时大声失败，不静默跳过** —— 静默跳过的集成测试等于没有。

代码里的阈值（基线容差、段落间距、空行判定、列间隙）都标注了实测来源与夹逼区间，
每一个都有一对测试从两侧把它夹住。改阈值前先看那两条测试。
