---
name: dotnet-kb
description: Use BEFORE writing or modifying any C#/.NET code - queries the local official-documentation knowledge base for authoritative API signatures and patterns, then falls back to official-domain web search and open web search when the knowledge base has no match, and requires labelling every line with which tier it came from. Triggers on any C#, .NET, ASP.NET Core, or .cs file work.
---

# .NET 官方文档知识库（本地，确定性）

本地有一份从 Microsoft 官方文档 PDF 提取的知识库，**它是第一权威**；查不到再联网，模型自身知识是最后兜底。

```
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe   工具
D:\pdf\jsonl                                          知识库（默认路径，也可设环境变量 DOCREF_KB）
```

**命令必须写全路径。** 它在 cove 的插件目录里、**不在 PATH 上**，只写 `docref.exe` 会直接 command not found。
换机器或换用户名时，上面那个路径等于 `%USERPROFILE%\.cove\plugins\docref\bin\docref.exe`
（bash 里写 `$USERPROFILE/.cove/plugins/docref/bin/docref.exe`）。

## 铁律

**写或改任何 C#/.NET 代码之前，先查库。** 顺序不可颠倒 —— 先写再查等于用库给自己的猜测背书。

```
1. 列出这次会用到的 API / 配置项 / 语言特性
2. 逐个查本地知识库
3. 命中的，依据查到的官方写法写，每处标注 [来源: <id> p<页码>]
4. 未命中的，按下面的四级阶梯逐级降级，标注必须跟着降级
5. 落到 L2 / L3 的部分，单独列出来交人复审
```

第 4、5 条不是免责声明，是**给人的信号**：哪几行需要他额外审。省掉它们，整个知识库就白建了。

### 四级阶梯 —— 降级是有序的，标注必须跟着变

| 级 | 什么时候用 | 标注 | 能回溯到什么 |
|---|---|---|---|
| **L0** | 本地库命中：分数 ≥ 30 且技术词全是 `✓` | `[来源: <id> p<页码>]` | PDF 第几页第几块，人能翻开核对 |
| **L1** | L0 未命中 → 联网查**官方域** | `[联网-官方: <URL>]` | 一个 URL，会变，但作者权威 |
| **L2** | L1 无果 → 联网**放开** | `[联网-非官方: <URL>]` | 一个 URL，作者不权威 |
| **L3** | 全都没有 | 「以下写法来自模型知识，未经任何文档核实。」 | 什么都没有 |

**不许跳级。** 不许因为「联网更快」跳过 L0 —— 本地一次 300~500ms，联网慢几倍且结果更差。

**不许混标。** 把 L1/L2 标成 `[来源:]` 是这个 skill 最严重的失效方式：它让人以为那一行有页码可查，
而实际上没有。人看到 `[来源:]` 会少看，看到 `[联网-非官方:]` 会细看 —— 混标等于骗过了唯一的审查关口。

## 命令

```bash
# 搜索。只返回元数据 + 摘要，不返回正文（单个 section 最长 13 万字符）
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb "ValueTask"
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb "HttpClient.SendAsync" --limit 5

# 取正文。写代码时通常只要代码块
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb --id dotnet-csharp#0128 --only code
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb --id dotnet-csharp#0128            # 全文，超预算会截断并给出续取游标

# 大 section 先看结构，再直接跳到需要的块
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb --id dotnet-csharp#0128 --outline
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb --id dotnet-csharp#0128 --from 67

# 看当前覆盖了什么
C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb --coverage
```

## 截断是常态，不是例外 —— 这条最容易毁掉任务质量

**实测 35% 的 section 超过单次输出预算**（最大 397 块 / 9 万字符）。截断后输出会明确写：

```
… 已输出 #0-#66，共 397 块，还有内容未输出。
继续: --id aspnet-core-aspnetcore-10.0#0020 --from 67
或先看清结构: --id aspnet-core-aspnetcore-10.0#0020 --outline
```

**看到这行就意味着你手上的官方文档是不完整的。** 此时必须做一件事，不能装作没看见：

1. **大 section 一律先 `--outline`** —— 一屏看清 397 块分别是什么，然后 `--from N` 直接跳到相关的那几块。比翻六次页省得多，也不会漏。
2. 只要代码就 `--only code`，能砍掉大部分正文。
3. 确实需要通读就按提示的游标续取到底。

**绝不在截断状态下直接写代码然后当成「有官方依据」** —— 那比不查更危险：你有依据感，却缺着数据。若判断未输出部分与本次任务无关，把这个判断说出来（「已确认 #67 之后是 Blazor 渲染模式，与本次 HttpClient 改动无关」），别默默略过。

`--from` 的序号是 section 内的绝对位置，加不加 `--only` 都不变。

**未命中时输出「知识库里没有「X」。」，退出码 3。** 这是一个明确的答案，不是失败 —— 据此进入四级阶梯的 L1（联网查官方域）。

## 查询是怎么匹配的（决定你该怎么提问）

**可以直接用自然语言提问。** 查询串会按空白和中英边界切成检索词：

```
$ C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb 什么是signalr
「什么是signalr」（检索词: 什么是 ✓ | signalr ✓） 命中 3 条：
```

三条规则必须知道：

**① 拉丁字母的检索词按词边界匹配。** `EF` 不会匹配 `reference` / `default` 里的 `ef`。
所以查缩写是安全的，不会被无关词淹掉。

**② 不做中文分词。** 连续中文保持整块 —— `中心怎么配置` 是一个检索词，不会切成四个词。
**所以提问时务必带上英文技术词**：`SignalR 怎么配置` 有效，纯中文的 `实时通信怎么配` 很可能未命中。

**③ 看 `✓` / `✗` 标记。多词查询里任何技术词标了 `✗`，就等于库里没有那个东西**，
不管上面列出了几条命中：

```
$ C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb 什么是Mapster
「什么是Mapster」（检索词: 什么是 ✓ | Mapster ✗） 命中 1 条：
  [   3] …  什么是持续集成？
注意: Mapster 在知识库里零命中 —— 上面的结果来自其他检索词，不代表库里有这些内容。
```

这一条最容易踩：命中列表看着有东西，实际上你要查的那个词根本不存在。
**`✗` 出现在技术词上 → 该词视同未命中，进入四级阶梯的 L1。**

## 命中 ≠ 有实质内容：看分数

搜索结果前面的 `[分数]` 决定这条命中值不值得信：

| 分数 | 含义 | 怎么用 |
|---|---|---|
| 100 以上 | 标题或路径命中，**整节在讲这个** | 权威依据，放心引用 |
| 30 ~ 100 | 符号精确命中或正文反复出现 | 可用，但要打开看是否真的讲到你要的点 |
| 30 以下 | **只是顺带提了几次**，那一节在讲别的 | 视同未命中，进入四级阶梯的 L1 |

实测例子（2026-09-11 复测）：`HttpClient` 157 分（整节在讲）、`FluentValidation` 4 分（只是出现过这个词）。
**拿一句顺带提及当官方依据，比不查更糟** —— 它会让人误以为这段经过核实。

## 覆盖面：不要猜，查

覆盖面会变（文档是持续补充的），**所以这里不列清单**。任何写死的清单都会过期，并且会劝你别去查一个其实已经有的东西。

- 不确定某主题在不在库里 → 直接查。一次查询 300~500ms，比猜便宜。
- 想看全貌 → `C:\Users\Lenovo\.cove\plugins\docref\bin\docref.exe kb --coverage`。

## 联网降级：怎么查、什么算无果

**前提是本地库已经查过。** 触发降级只有三个信号，而且**退出码单独看不够** —— `✗` 和低分这两种情况退出码都是 0：

| 信号 | 判定 | 退出码 |
|---|---|---|
| `知识库里没有「X」。` | 未命中 | 3 |
| 技术词标 `✗` | 该词未命中，不管上面列了几条结果 | **0** |
| 分数 < 30 | 只是顺带提及，视同未命中 | **0** |

### L1：官方域

用联网搜索工具（cove 里叫 `websearch`，Claude Code 里叫 `WebSearch`），查询里带上域限定：

```
IHttpClientFactory retry policy site:learn.microsoft.com
Mapster ProjectToType site:github.com
```

**「官方」指的是那个东西的作者，不是微软。** 这一点最容易搞错。算 L1 的有：

- `learn.microsoft.com`、`devblogs.microsoft.com`、`github.com/dotnet/*` —— 微软自家的东西
- **该库自己的仓库或文档站** —— Mapster 的官方是 `github.com/MapsterMapper/Mapster`，
  FluentValidation 的官方是 `docs.fluentvalidation.net`。它们不在微软域上，但它们就是权威。

这正是本地库缺口的形态：**那些缺口都没有官方 PDF，但都有官方网页。** L1 就是为它们准备的。

### L2：放开

L1 无果才放开。无果的判定是：搜索返回空，或返回的页面标题与摘要里根本没有你查的那个符号。

放开之后 StackOverflow、博客、各种镜像都能用，但标注必须是 `[联网-非官方:]`，一处都不能省。

### 为什么标注必须分级

本地库的每条都带 `page` + `ord`，能指回 PDF 的第几页第几块，**人可以翻开原文核对**。
联网结果没有这个属性：URL 会失效、页面会被改、内容可能本来就错。所以：

- **L1 / L2 引用的代码必须过编译器和测试。** L0 也该过，但 L1/L2 是没有退路的必须。
- 不要把联网查到的写法说成「官方文档写的」，除非它确实来自 L1 —— 那就老实标 `[联网-官方:]`，不是 `[来源:]`。
- 联网也没有，就走 L3。**查不到就说查不到**，比给一个看起来有出处的错答案好得多。

## 标注格式

三种标注，一眼能看出这一行的依据有多硬：

```csharp
// [来源: dotnet-csharp#0128 p759-769]                          ← L0 能翻回 PDF 这一页
await foreach (var item in GetItemsAsync(token))

// [联网-官方: https://github.com/MapsterMapper/Mapster#readme]  ← L1 作者自己的文档
config.NewConfig<Order, OrderDto>().Map(d => d.Total, s => s.Amount);

// [联网-非官方: https://stackoverflow.com/a/12345678]           ← L2 必须过编译和测试
```

L0 的 `id` 和页码能回溯到原始 PDF 的具体页 —— 这是本地库区别于模型知识和联网结果的关键：**可验证**。
L1 / L2 只能回溯到一个 URL，所以它们的标注**必须长得不一样**，否则可验证性就被稀释成了装饰。

最后把依据分级集中列一遍给人看，别指望他自己从代码里翻标注：

```
本次改动的依据分级：
  L0 本地库      3 处
  L1 官方网页    1 处（Mapster 映射配置）
  L2 非官方      0 处
  L3 未经核实    1 处 —— HttpClient.Timeout 的语义，任何文档里都没查到
```

## 已知限制（引用时必须知道）

**PDF 排版折行已还原**（长代码行被折断后会接回同一行），但还原是启发式的 ——
29.9 万行代码里仍有约 0.007% 偏长（最长 404 字符）。
**引用的代码要过编译器和测试**，这本来就该做。

其他限制：表头行的列可能粘连（`C# type keyword.NET type`）；
部分行内代码（超链接形式的）未标注反引号；提示框 NOTE/TIP/WARNING 目前是普通文本。

## 反合理化

这些念头出现时就是在偷懒，而它们正是这个 skill 要治的病：

| 念头 | 现实 |
|---|---|
| 「这个 API 我很确定」 | 模型对 API 的确定感与正确率不相关。查一次 300ms。 |
| 「这么简单不用查」 | 简单的东西恰恰查得快。省下的是秒，赌上的是一次幻觉 API。 |
| 「先写完再查一遍」 | 顺序反了。写完再查是拿库给自己的猜测背书，而不是让库指导写法。 |
| 「库里应该没有，不查了」 | 「应该没有」是猜。覆盖面在变，查一次就有确定答案。 |
| 「查到了但和我想的一样，不用标来源」 | 标来源是给人看的审查线索，不是给你自己的记录。 |
| 「有命中就是有覆盖」 | 看 `✓`/`✗` 和分数。技术词标 `✗`、或分数低于 30，都等于没有。 |
| 「截断了但我看到的够用了」 | 你不知道没看到的是什么。要么续取，要么明确说出「已确认剩余部分无关」。 |
| 「库里没有，我就正常写吧」 | 必须显式声明「未经核实」。不声明，人就无法知道哪段需要额外审。 |
| 「这是重构，不算写新代码」 | 改动涉及的 API 一样要核实。重构最容易悄悄改变语义。 |
| 「联网查到了，就等于有官方依据」 | 看是 L1 还是 L2。把 L2 说成官方是这个 skill 最严重的失效方式。 |
| 「StackOverflow 高票答案够权威了」 | 高票 ≠ 正确，更 ≠ 当前版本。它是 L2，必须过编译和测试。 |
| 「联网比查库快，先联网」 | 反了。本地一次 300~500ms 且带页码；联网慢且不可回溯。跳级是把可验证性白扔。 |
| 「三种标注差别不大，统一标 [来源:] 省事」 | 那等于把联网结果伪装成官方页码。审查的人只有这一个信号可看。 |

## 什么时候不必用

- 纯粹改本项目自有代码、不涉及任何框架/BCL API（改个变量名、调整自有类的内部逻辑）
- 用户明确说了不用查
