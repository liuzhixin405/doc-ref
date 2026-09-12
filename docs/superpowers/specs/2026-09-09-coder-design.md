# coder — 降级智能体设计

**日期**：2026-09-09
**状态**：待评审
**首版语言**：C#

---

## 1. 定位

**一个按语言组织、以确定性知识库驱动的编码智能体。**

它是智能体，有完整的 `观察 → 决策 → 行动 → 观察结果` 循环。区别只在于**每一步的决策者与执行者是确定性知识，而不是 LLM**。只有当知识库无法决策时，才降级到 AI（本地或联网）。

用一句话概括它与现有 AI 助手的关系：**同一个循环，把每一步的智能来源从「模型权重里的概率」换成「可查询的确定数据」。**

### 范围是一门语言，不是一个项目

知识按**语言**组织和收集。首版锁 C#，且它对**任意** C# 项目成立。这不是为某个具体代码库定制的工具。

## 2. 问题

现有 AI 编码助手的失败模式**不是无知，是过剩且无边界**：

- 一行能写完的写成多行，一律按「最优」而非「够用」来写。
- 加一个字段能给你抽三层接口 + 工厂 + 配置类。
- 实现本身没错，但复杂晦涩，读不懂也不好维护。
- 编造不存在的 API，因为它的「知识」是概率而非事实。

根因是它没有边界：权重里什么都有，而且没有任何机制阻止它把所有花活都用上。

## 3. 知识分级 —— 系统的核心

**等级 = 可用知识切片 = 复杂度天花板。这三者是同一件事。**

设成初级档，系统里根本不存在 `Span<T>`、`ValueTask`、`Interlocked` 这些东西可调用。它写不出晦涩代码不是因为不愿意，**是因为没有这个知识**。这把「反过度设计」从一条自觉守则变成了结构性约束。

### 3.1 C# 四档知识切片

| 档 | 语言与运行时 | BCL / 生态 | 判断力 |
|---|---|---|---|
| **L1 初级**<br>写得出能跑的代码 | 类型、控制流、类/接口/属性、`try/catch`、基础 `async/await`、LINQ 基础（`Where`/`Select`/`First`） | `string`、`List<T>`、`Dictionary<K,V>`、`DateTime`、`File`、`HttpClient` 基础用法 | 编译要通过；异常要处理 |
| **L2 中级**<br>写得出模块，知道常见坑 | 泛型与约束、`IDisposable`/`using`、可空引用类型、`record`、`struct` vs `class`、LINQ 延迟执行、DI | ASP.NET Core、EF Core、序列化、单元测试、常用 NuGet | `async void` 禁用、`.Result` 死锁、`CancellationToken` 传递、集合选型（`HashSet` vs `List`） |
| **L3 高级**<br>懂权衡，知道什么不该做 | 并发原语（`lock`/`SemaphoreSlim`/`Interlocked`/`Channel`）、`Span<T>`/`Memory<T>`、`ValueTask`、`IAsyncEnumerable`、表达式树、Source Generator | `ArrayPool`、BenchmarkDotNet、EF Core 查询行为 | 装箱代价、GC 行为、N+1、线程安全设计、何时**不要**优化 |
| **L4 专家** | `unsafe`/指针、P/Invoke 互操作、`Vector<T>` SIMD、IL 生成、Roslyn 分析器编写 | 无锁数据结构、内存屏障、JIT / 分层编译行为 | 运行时内部机制；何时可以违反上面所有规则 |

**默认 L1。** 升档必须显式指定并给出理由。

### 3.2 分级表本身怎么维护 —— 可行性的关键

这里是整个方案能不能成立的地方。答案是：**分级表是人工的，知识本体是自动提取的。**

分级表只需要在**命名空间 / 类型 / 语言特性**这个粒度上做归类白名单，不是逐个成员标注：

```yaml
# tiers/csharp.yaml
L1:
  namespaces: [System, System.Collections.Generic, System.IO, System.Text]
  types:      [System.Net.Http.HttpClient]
  features:   [class, interface, property, try-catch, async-await, linq-basic]
L2:
  inherits: L1
  namespaces: [System.Linq, Microsoft.Extensions.DependencyInjection, Microsoft.EntityFrameworkCore]
  features:   [generics, nullable-reference-types, record, disposable]
L3:
  inherits: L2
  namespaces: [System.Threading, System.Threading.Channels, System.Buffers]
  types:      [System.Span`1, System.Memory`1, System.Threading.Tasks.ValueTask]
  features:   [expression-trees, source-generators, stackalloc]
L4:
  inherits: L3
  unrestricted: true
```

规模是**几百条条目，一次性工作，量级是几天而不是几个月**。而这几百条背后挂着的几十万个 API 签名、两千条诊断规则，全部是自动提取的（见 §4）。

分级表可版本化、可 review、可按团队口味调整。它是这个系统里唯一需要人工判断的知识资产。

## 4. 知识来源 —— 四层，获取方式完全不同

| 层 | 是什么 | 从哪来 | 人工成本 |
|---|---|---|---|
| **A 符号知识**<br>"有什么可用、签名是什么" | BCL 与 NuGet 包的全部公开 API、精确签名、XML 文档、引入版本 | Assembly metadata / `System.Reflection.Metadata` 自动提取 | **零**。成本在工具，不在内容 |
| **B 诊断知识**<br>"什么是错的、怎么修" | 编译错误码（CS××××，约 2000 条）、分析器规则（CA / SA / S××××）、每条的成因与修法 | Roslyn 自带的 `DiagnosticDescriptor` + 现成的 `CodeFixProvider` + 官方文档 | **低**。半自动提取 |
| **C 模式知识**<br>"这类事情怎么做" | 通用 C# 任务的标准做法：ASP.NET Core 加 endpoint、EF Core 加实体、`HttpClient` 带重试调用、后台服务…… 绑技术栈，不绑项目 | 人写 + 审核，按档位给变体 | **唯一的瓶颈**。所以少而精 |
| **D 项目上下文**<br>"当前这个项目长什么样" | 现有分层、命名、已引用的包、已有的同类代码 | Roslyn **运行时读**当前 solution | **零**。不预存，不是知识资产 |

两个要点：

**① A 和 B 是免费的。** 它们不需要有人去写文档 —— 一个是从程序集元数据提取，一个是 Roslyn 自己就带着。所以「收集 C# 知识」不是一个要写几万条的无底洞，绝大部分是**提取**出来的。

**② Roslyn 本身就是 C# 知识的可执行形式。** 它内部装着完整的语言语义（绑定、重载解析、类型推导）、全部诊断规则、几百个确定性代码修复。很多「知识」不需要写成数据，而是**调用 Roslyn 的能力**。

**为什么不用向量库**：A/B/C 三层全部是精确索引 —— 符号名、错误码、规则号、模式 id。向量检索永远返回 top-k、永远「有答案」，结构上无法表达「库里没有」；而本系统的核心价值恰恰在于能明确说出「没有，我要降级到 AI」。相似度 0.72 既不算命中也不算未命中。API 签名差一点就是错的。

## 5. 降级循环

```
需求
 │
 ├① 意图解析 ──────── 显式命令；或本地小模型只出参数、不出代码
 │
 ├② 查 C 层：有匹配的模式吗？（受当前档位切片限制）
 │      命中 → 按档位执行                                 【零 AI】
 │
 ├③ 查 A 层：模式里用到的 API 存在吗、签名对吗？
 │      确定性填充；不存在 → 立刻失败，不猜               【零 AI】
 │
 ├④ 执行：模板渲染 / Roslyn AST 变换
 │
 ├⑤ 编译（Roslyn 内存编译）
 │      有诊断 → 查 B 层拿确定性修复 → 重编译 ⟲           【零 AI，自愈】
 │
 ├⑥ 档位合规检查：产出里有没有超出当前档位切片的东西？
 │      有 → 拒绝并报告                                   【零 AI】
 │
 └⑦ 到这儿仍未解决 → 降级到 AI（本地 / 联网）
```

第 ⑤ 步是这个设计最有力的地方：**编译错误码 → 查 B 层 → 确定性修复 → 重编译**，一个不需要 LLM 的自愈循环。现有 agent 修编译错误靠把报错扔给 LLM 让它猜，而 `CS0246` 该怎么修是**确定的因果知识**。

第 ⑥ 步是档位的强制关口。它保证「初级档产出初级代码」不是靠自觉，而是**产出物要过静态检查**：用到了切片外的类型就是不合规。

## 6. AI 降级：两个活，两种模型

两件事难度差一个量级，必须分开：

### 6.1 听懂人话 → 本地小模型（1~3B，Ollama 即可）

```
"加一个带重试的 HTTP 调用"
        ↓
pattern=http-client-retry  level=L1
```

它**只产出参数，不产出代码**。参数摆在眼前，错了一眼看见。这活儿是分类 + 填槽而非创作，小模型足够。

### 6.2 知识库里真没有 → 才动大模型，且产出的是**知识**而非代码

```
未命中 → 明确报告「C 层没有 X 模式」/「A 层没有 Y API」
   ↓  人批准降级
AI 起草一条新模式（含档位归属）
   ↓
用同一套档位合规检查 + 编译验证它       ← AI 的错在此拦住
   ↓  人审核
入库 → 下次这个需求零 AI 参与
```

两个性质：

- **AI 的错误在入库关口被拦截**，不流进代码库。新模式自己也得过检查。
- **AI 用得越多，以后用得越少。** 现有 AI 是反的 —— 用得越多越依赖，因为它什么都不沉淀。

## 7. 架构：当编译器写

本工具本质上是一个编译器：输入「意图 + 参数 + 档位」，输出「代码改动」。采用编译器架构（前端 → IR → 校验 pass → 后端），不用管道 / Behaviors。

### 7.1 为什么不用管道

管道的 Behavior 之间靠共享的可变 `Context` 通信，它会退化成上帝对象 —— 谁都往里塞字段，最后无人知道某字段是哪一关设置的；控制流也是隐式的，必须读完所有 Behavior 才知道谁短路了谁。

**判据一：函数签名就是依赖声明。** `Resolve(Pattern, Tier)` 明摆着只需这两样，多读一个都编译不过。上帝对象把依赖声明全擦掉了。

**判据二：好抽象的标准是「它让某整类问题消失了吗」。** IR（`ChangePlan`）让四件事同时白送：dry-run、diff 预览、单元测试（断言 IR 内容而不必真编译）、以后换语言只换后端；并且**回滚逻辑彻底不需要**（落盘是最后一步）。它消掉的复杂度远大于引入的。

### 7.2 IR

```csharp
record ChangePlan(
    IReadOnlyList<FileCreate> Creates,   // 新文件：路径 + 渲染后内容
    IReadOnlyList<SymbolEdit> Edits,     // 改现有文件：目标符号 + 编辑动作
    IReadOnlyList<string>     UsedApis   // 用到的 API 全名，供档位合规检查
);

record FileCreate(string RelativePath, string Content);
record SymbolEdit(string Symbol, EditKind Kind, string Payload);
enum EditKind { AddInterface, AddProperty, AddAttribute, AddConstructorParameter, AddMethod }
```

`UsedApis` 是档位检查的输入 —— 它让「产出有没有超档」变成一次集合包含判断。

### 7.3 阶段：纯函数 + Result 链

```csharp
Result<Request>     Parse(string[] argv);
Result<Pattern>     MatchPattern(Request r, Tier t);       // C 层查询
Result<Unit>        ResolveApis(Pattern p, Tier t);        // A 层查询 + 档位切片校验
Result<ChangePlan>  Plan(Pattern p, Request q, Workspace w);
Result<Compilation> Apply(ChangePlan p, Workspace w);      // 内存，不落盘
Result<Unit>        Heal(Compilation c);                   // B 层：诊断 → 确定性修复 ⟲
Result<Unit>        CheckTier(ChangePlan p, Tier t);       // 档位合规
Result<Unit>        Flush(ChangePlan p);                   // 落盘
```

驱动它的是一行，控制流一眼看完：

```csharp
Parse(argv)
  .Bind(r => MatchPattern(r, tier))    // 未命中 → Halt，进 AI 降级
  .Bind(p => ResolveApis(p, tier))     // API 不存在 → Halt，不猜
  .Bind(Plan)
  .Tap(ShowDiffAndConfirm)             // dry-run：ChangePlan 直接渲染给人看
  .Bind(Apply)
  .Bind(Heal)
  .Bind(p => CheckTier(p, tier))
  .Bind(Flush);
```

`Result<T>` 自己写（约 60 行），不引 FP 库。`Halt` 必须携带**原因**与**建议的降级动作** —— 短路是一等公民，不可能像向量检索那样含糊过去。

### 7.4 扩展点局部化

**判据三：该活的地方活，该死的地方死。** 主干八步固定写死，没有插件注册、没有反射扫描。真正会不断增加的只有三处，只在这三处给列表：

```csharp
List<Pattern>      patterns;      // C 层，会一直加
List<IHealRule>    healRules;     // B 层修复规则，会一直加
Dictionary<Tier, Slice> tiers;    // 分级表，会调整
```

## 8. 技术栈与目录

| 部件 | 选择 | 理由 |
|---|---|---|
| 工具本身 | C# / .NET 8 | 要做 C# 的语义分析，Roslyn 是唯一严肃选项，而 Roslyn 的家在 .NET |
| 语义分析 / AST 编辑 / 内存编译 | `Microsoft.CodeAnalysis`（Roslyn） | 读**真实符号表**，不是文本猜测 |
| 符号知识提取 | `System.Reflection.Metadata` | 从程序集直接读元数据，不加载程序集 |
| 模板渲染 | Scriban | 轻，`{{ }}` 语法 |
| 知识与分级表格式 | YAML（YamlDotNet） | 人可读可手改可 review |
| 入口 | `dotnet tool`，显式命令 | 零歧义。自然语言层是后加的一薄层 |
| 测试 | xUnit | — |

**只拆 3 个 project。** 不按 Core / Roslyn / Knowledge / Templates 拆四五个 —— 等真的大到痛了再拆，否则就在一个专治过度设计的项目上过度设计：

```
D:\github\coder\
├── Coder.sln
├── docs/superpowers/specs/
├── knowledge/
│   └── csharp/
│       ├── tiers.yaml              # 分级表（唯一的人工判断资产）
│       ├── symbols/                # A 层，自动生成，不手改
│       ├── diagnostics/            # B 层，半自动生成
│       └── patterns/               # C 层，人写
│           └── http-client-retry/
│               ├── pattern.yaml
│               └── templates/*.sbn
├── src/
│   ├── Coder.Cli/                  # 参数解析 + 输出呈现
│   └── Coder.Engine/               # Result / 知识查询 / ChangePlan / Roslyn / 渲染
└── tests/
    └── Coder.Tests/
```

**换语言的路径**：`knowledge/<language>/` 平行增加；`Apply` / `Heal` 换后端实现；分级表格式、模式格式、IR 全部不变。首版只做 `csharp`。

## 9. 首版范围

**做：**

1. `Result<T>` + `HaltReason`（携带原因 + 建议降级动作）
2. **A 层提取器**：从程序集元数据生成符号索引（先做 .NET 8 BCL），支持精确查询「这个 API 存不存在、签名是什么」
3. **分级表**：`tiers.yaml` 四档 C# 切片（§3.1 的内容落成数据）
4. **档位合规检查**：给定一段代码或 `ChangePlan`，判定是否用到了切片外的 API / 语言特性
5. **B 层最小可用**：提取 Roslyn 的 `DiagnosticDescriptor` 全集；实现 5~10 条最常见编译错误的确定性修复规则（缺 `using`、类型不匹配、缺成员实现等）
6. **C 层 3~5 个模式**：证明模式格式可用即可，不求覆盖
7. 完整降级循环 `②→⑥` 端到端跑通，`⑦` 只输出「需要降级」的报告而不实际调 AI
8. `--dry-run`（默认）与 `--apply`

**不做（后续阶段）：**

- Phase 2：C 层规模化；B 层修复规则规模化
- Phase 3：本地小模型意图解析（§6.1）
- Phase 4：AI 起草新知识 + 入库审核流（§6.2）
- Phase 5：第二门语言
- D 层项目上下文适配（见 §11 待定项）

**首版验收标准：**

1. 问「`HttpClient.SendAsync` 的重载有哪些」能从 A 层拿到精确签名列表；问一个不存在的 API 能明确答「不存在」而不是给个相似的。
2. 同一个模式在 L1 与 L3 下产出可见不同的代码，且 L1 产出**不含**任何 L3 切片里的类型。
3. 故意造一个缺 `using` 的产出，`Heal` 能零 AI 修复并重编译通过。
4. 任何一步无法用知识库解决时，输出的 `Halt` 说得清「缺哪一层的什么」。

## 10. 测试策略

- **`ChangePlan` 层单测**（主力）：给定模式 + 参数 + 档位，断言产出的 `Creates` / `Edits` / `UsedApis`。不必真编译，快且稳 —— 这是 IR 白送的好处。
- **档位检查单测**：手工构造用了 `Span<T>` 的代码，断言在 L1 被拒、在 L3 通过。
- **`Heal` 单测**：构造带已知诊断码的源码，断言修复后诊断消失且未引入新诊断。
- **A 层查询单测**：已知存在的 API 必须命中；已知不存在的必须明确未命中（**假阳性是缺陷** —— 一个会瞎猜的符号库比没有更糟）。

## 11. 待定项

**D 层（项目上下文适配）要不要做。** 两种取向会导致不同的工具：

- **不做**：coder 只按知识库产出标准的、地道的 C# 代码，不试图迎合每个项目的自定义约定。工具行为完全可预测，但产出可能与宿主项目现有风格不一致，需要人工调。
- **做**：coder 运行时用 Roslyn 读当前 solution，让产出贴合项目已有的分层与命名。产出更能直接融入，但引入了「读出来的约定」这个不确定输入，且每个项目表现不同。

首版按**不做**推进（更符合「不为某个项目服务」的定位，也更简单）。这一项需要确认。
