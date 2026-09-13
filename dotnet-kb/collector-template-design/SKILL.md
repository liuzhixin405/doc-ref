---
name: collector-template-design
description: Use when designing a reusable data-collector template — a WebSocket stream/market-data adapter (contract interface + template-method base + per-source strategy + parser) and the polling BackgroundService collectors behind it — by separating the invariant collection lifecycle from per-source protocol details. Triggers when the user asks to 设计/抽象采集器、WebSocket 行情适配器、K线/行情/资讯采集、从零设计可复用采集模板、新增一个数据源 handler, or mentions 模板方法、策略模式、可复用模板、ExchangeHandlerBase、BackgroundService 采集 — even if they never say "skill".
---

# 可复用采集器模板设计

这个 skill 讲的是**一套通用设计方法**：怎么把「同类多实例的采集器」设计成可复用模板。

两个具体化范例：

1. **WebSocket 数据源适配器**（如交易所行情 handler）—— 抽象到「加一个新数据源只写 4 个方法」。
2. **轮询型采集 BackgroundService**（如 K 线采集）—— 背后的设计思想。

目标不是「照着某个项目的代码抄」，而是**从零推导出这套模板的方法** + 模板长什么样。
文末附一个真实实现的对照（FXH Spider），标出哪里走样了，供重构参考。

## 先说清楚：下面出现的名字都是占位符

这套设计用中性占位名描述，不绑定任何具体项目。照着搭的时候把占位名换成你自己的：

| 占位名 | 指什么 | 换成（FXH Spider 里的真名） |
|---|---|---|
| `XxxExchange` / `XxxHandler` | 某个数据源的适配器 | `MexcGateioHandler`（注意：它把两个源合一了，是反例） |
| `Symbol` | 交易对 / 标的模型 | `SymbolModel` |
| `StoreKline` | 入库用的 K 线模型 | `Kline` |
| `PushKline` | 推送用的 K 线模型 | `BinanceKline` |
| `IStore` | 落库仓储 | `IMongoRepository` |
| `ICache` / `IPublisher` | 缓存 / 发布订阅 | `IRedisService` |
| `INotifier` | 失败告警 | `LarkBotHelper` |
| `SymbolCacheService` | 维护交易对缓存的服务 | `CacheKlineSymbolService` |
| `KlineApiServiceBase` | REST 采集基类（建议新增） | 无（现有是三个平铺服务） |
| `ServiceSelector` | 启动开关（配置键） | `StartService` |

**保留不换**的是模式自身的词汇：`IExchangeHandler` / `ExchangeHandlerBase` / `ExchangeService` /
`BackgroundService` / `ConcurrentQueue`。这些是模式的骨架，不是项目名。

---

## 一个总纲：不变沉淀成模板，变化变成策略

任何「同类多实例」的采集，设计套路都一样：

> 1. 先写死**一个**真实数据源，跑通。
> 2. 写**第二个**同类源时，把两段代码并排，圈出「一模一样」和「不一样」。
> 3. 一模一样的 → 下沉成**基类模板**；不一样的 → 变成**抽象方法 / 策略**，签名对齐。
> 4. 最易变、改最勤的部分（报文/行列解析）单独抽出去。
> 5. 外面套一个 `BackgroundService` 接进宿主，靠开关配置独立启停。

下面两个设计都是这个总纲的具体化。

---

## 第一部分：WebSocket 数据源适配器的可复用设计

### 问题

要采 N 个数据源（N 个交易所、N 个流）的实时数据。每个源的 WebSocket 协议都不同（URL、订阅报文、
返回报文、标的命名、心跳格式），但「采数据的生命周期」完全一样。

### 手法：模板方法（Template Method）+ 策略（Strategy）

先明确**不变**和**变**的边界。这一步是整个设计的钢筋：

**不变的生命周期（每个数据源都一样）**：

```
拿标的列表 → 分组 → 连 WebSocket → 发订阅 → 循环收数据 → 解析成对象 → 入缓冲队列
  → 后台任务消费缓冲（落库 / 推送）→ 断线重连
```

**变体点（每个数据源不一样）**：

| 变体 | 例子（行情场景） |
|---|---|
| 连接地址 | `wss://.../ws`（每所不同） |
| 订阅报文 | `{"method":"SUBSCRIPTION","params":[...]}` vs `{"channel":"...","event":"subscribe"}` |
| 返回报文解析 | 取 `d.k` vs 取 `result` |
| 标的命名 | `BTC_USDT`（REST）vs `BTCUSDT`（WS 订阅） |
| 心跳报文 | `{"event":"ping"}` vs `{"channel":"spot.ping"}` |
| 分组大小 | 每源不同（WS 有订阅数量上限） |

### 五层结构

```
IExchangeHandler          契约：只声明「做什么」，给宿主/DI 看
   ▲
ExchangeHandlerBase       模板：实现全部「不变」，变体声明成 abstract
   ▲
XxxHandler                策略：每个数据源一个类，只 override 变体
   │（调用）
XxxMessageParser          解析：最易变，单独隔离
   ▲
ExchangeService : BackgroundService   宿主适配：把 handler 接进宿主生命周期
```

为什么**既有接口又有抽象基类**（两层抽象）：
- 接口是给**外面**（`ExchangeService`、依赖注入）看的契约——宿主只依赖 `IExchangeHandler`，不关心具体是哪个源。
- 抽象基类是给**实现者**看的模板——它把生命周期骨架和重连逻辑免费送给每个数据源，实现者只填变体。

### 从零推导的 5 步

1. **写死一个源**，完整跑通「连→订→收→解析→落库」。
2. **写第二个源**，两段代码并排，用上表圈出「一模一样」（生命周期）和「不一样」（协议）。
3. **下沉**：一模一样的部分搬进 `ExchangeHandlerBase`，写成 `StartAsync` 模板方法 + 重连逻辑。
4. **抽象**：不一样的部分在基类里声明成 `abstract`，签名对齐（统一 `SubscribeAsync(socket, symbols)`，
   而不是 `SubscribeToSourceA(...)` / `SubscribeToSourceB(...)`）。
5. **隔离解析**：把「原始报文 → 对象」抽成 `XxxMessageParser`（最常改，别散在 handler 里），
   最后套 `ExchangeService` 进宿主。

### 可复用模板

```csharp
// 第 1 层：契约 —— 只声明「做什么」
public interface IExchangeHandler
{
    string ServerName { get; }          // "source-a" / "source-b" ...
    string ServerUrl { get; }           // wss://...
    string KlineInterval { get; }       // 周期，如 "1m" / "1d"

    Task StartAsync(CancellationToken ct);          // 模板方法（基类实现）
    Task<IReadOnlyList<Symbol>> GetSymbolsAsync();
    Task<IReadOnlyList<IReadOnlyList<Symbol>>> GroupSymbolsAsync(
        IReadOnlyList<Symbol> symbols, int groupSize);
    Task<ClientWebSocket> ConnectAsync();
    Task SubscribeAsync(ClientWebSocket socket, IReadOnlyList<Symbol> symbols);
    Task ReceiveDataAsync(ClientWebSocket socket);
    Task<Symbol> GetSymbol(string symbol);
}

// 第 2 层：模板 —— 实现全部「不变」，变体是 abstract
public abstract class ExchangeHandlerBase : IExchangeHandler
{
    public abstract string ServerName { get; }
    public abstract string ServerUrl { get; }
    public abstract string KlineInterval { get; }
    public abstract CancellationTokenSource CancellationTokenSource { get; set; }
    public abstract ILogger Logger { get; }

    protected virtual int GroupSize => 30;                       // 变体（带默认值）
    protected readonly ConcurrentQueue<PushKline> Buffer = new(); // 收包线程 → 消费线程

    // 模板方法：生命周期骨架，所有数据源共用
    public async Task StartAsync(CancellationToken ct)
    {
        var symbols = await GetSymbolsAsync();                              // 1 拿标的（变体）
        var groups  = await GroupSymbolsAsync(symbols, GroupSize);          // 2 分组（变体）
        _ = Task.Run(() => ConsumeBufferAsync(ct), ct);                     // 8 消费缓冲（不变）
        foreach (var group in groups)
            await RunGroupAsync(group, ct);                                 // 3-7 + 重连（不变）
    }

    // 不变：一个分组内的「连→订→收」+ 退避重连
    private async Task RunGroupAsync(IReadOnlyList<Symbol> group, CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = await ConnectAsync();                     // 变体
                _ = Task.Run(() => SendHeartbeatAsync(socket, ct), ct);     // 心跳（不变）
                await SubscribeAsync(socket, group);                         // 变体
                await ReceiveDataAsync(socket);                              // 变体：收 + 解析 + 入 Buffer
                return;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"{ServerName} attempt {++attempt}");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);               // 不变：退避重连
            }
        }
    }

    // 不变：缓冲消费。落哪、推哪由钩子决定（变体）
    private async Task ConsumeBufferAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Buffer.TryDequeue(out var kline))
                await PublishAsync(kline);                        // 推送（变体钩子）
            // 攒批后 await SaveAsync(batch, ct);                 // 落库（变体钩子）
        }
    }
    protected abstract Task SaveAsync(IReadOnlyList<StoreKline> batch, CancellationToken ct);
    protected abstract Task PublishAsync(PushKline kline);

    // —— 变体：每个数据源实现 ——
    public abstract Task<IReadOnlyList<Symbol>> GetSymbolsAsync();
    public abstract Task<IReadOnlyList<IReadOnlyList<Symbol>>> GroupSymbolsAsync(
        IReadOnlyList<Symbol> symbols, int groupSize);
    public abstract Task<ClientWebSocket> ConnectAsync();
    public abstract Task SubscribeAsync(ClientWebSocket socket, IReadOnlyList<Symbol> symbols);
    public abstract Task ReceiveDataAsync(ClientWebSocket socket);
    public abstract Task<Symbol> GetSymbol(string symbol);
}

// 第 3 层：策略 —— 一个数据源一个类，只 override 变体
public class SourceAHandler : ExchangeHandlerBase
{
    public override string ServerName => "source-a";
    public override string ServerUrl => "wss://.../ws";
    public override string KlineInterval { get; }
    public override ILogger Logger { get; }
    // 构造注入 IStore / ICache + (url, interval, name)

    // ConnectAsync / SubscribeAsync / GroupSymbolsAsync / GetSymbol 各写几行
    // ReceiveDataAsync 只做：收文本 → SourceAMessageParser.Parse(raw) → 入 Buffer
}
public class SourceBHandler : ExchangeHandlerBase { /* 同上，各 override */ }

// 第 4 层：解析 —— 最易变，单独一个类
public static class SourceAMessageParser
{
    public static IEnumerable<PushKline> Parse(string raw, Func<string, Task<Symbol>> lookup)
    { /* 源 A 报文 → 对象 */ }
}

// 第 5 层：宿主适配
public class ExchangeService : BackgroundService
{
    private readonly IExchangeHandler _handler;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _handler.StartAsync(ct);
            await Task.Delay(TimeSpan.FromHours(12), ct);
        }
    }
}
```

模板的**价值判据**：加一个新数据源，你只写 `XxxHandler` 里的 4 个方法 + 一个 `Parser`，
连接、重连、心跳、缓冲、落库、推送全部白送。

---

## 第二部分：轮询型采集 BackgroundService 的设计思想

一套数据采集子系统通常是一堆独立的 `BackgroundService`。它的设计思想是**「采集什么 / 怎么采 / 采完送哪」三件事拆开，
通过标的缓存和存储串联，每个服务只干一件事、可独立启停。**

### 决策 1：标的缓存与采集解耦（`SymbolCacheService`）

「采集什么」（标的列表）由**一个独立服务**维护：查库 → 跨源去重 → 写缓存，带 TTL。所有采集服务**只读缓存**。

- 好处：换标的只改缓存服务，采集服务一行不动；多服务共享同一份标的。
- 判据：**「要采哪些标的」是一个会被多个采集器复用的数据，就该抽成一个上游服务。**

### 决策 2：入库模型 ≠ 推送模型

| | 落库 `StoreKline` | 推送 `PushKline` |
|---|---|---|
| 去向 | 持久存储 | 发布订阅 |
| 字段类型 | `double` / `DateTime` | `decimal` / `long` |
| 特性 | 存储序列化 | 消息序列化 |

两种序列化需求不同，**别共用一个模型**。混用（把 `List<PushKline>` 往收 `List<StoreKline>` 的方法里塞）
是「注释代码其实编译不过」的典型来源。

### 决策 3：REST 与 WS 双通道，互为补充

- **REST 轮询**：可靠、可补历史、兜底。
- **WS 推送**：实时、快。
- 两者**各是一个 BackgroundService，同时开**。

### 决策 4：一个周期 / 一个来源 = 一个 BackgroundService，独立启停

`1d` / `1m` / `5m`、`source-a` / `source-b`、`min` / `history` / `derive` 各自一个服务，
靠开关配置决定哪个进程跑哪几个。挂一个不影响别的。

### 决策 5：采集管线（poll loop pipeline）—— 轮询采集的统一模板

轮询型采集统一的流程：**读缓存 → 分组 → 批内并行抓取 → 解析 → 落库 → 跨日重置**。

```csharp
// 轮询型采集的可复用模板
public abstract class KlineApiServiceBase : BackgroundService
{
    // —— 变体：每个来源/周期 override ——
    protected abstract string BaseUrl { get; }                    // "https://.../kline?symbol={0}&interval={1}"
    protected abstract string SymbolCacheKey { get; }             // 读哪个标的缓存
    protected abstract string BuildSymbol(Symbol s);              // "BTC_USDT" vs "BTCUSDT"
    protected abstract IEnumerable<StoreKline> ParseRow(object[] row, Symbol s);
    protected abstract Task SaveAsync(List<StoreKline> klines, CancellationToken ct);

    // —— 不变：采集管线 ——
    protected virtual int BatchSize => 50;
    protected virtual TimeSpan Interval => TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var symbols = await ReadSymbolsAsync(ct);            // 1 读标的缓存
            if (symbols.Count == 0) { await Task.Delay(1000, ct); continue; }
            foreach (var batch in Batch(symbols, BatchSize))     // 2 分组
                await ProcessBatchAsync(batch, ct);              // 3+4+5 并行抓取+解析+落库
            await Task.Delay(Interval, ct);                      // 轮询
        }
    }

    private async Task ProcessBatchAsync(List<Symbol> batch, CancellationToken ct)
    {
        await Task.WhenAll(batch.Select(async s =>
        {
            try
            {
                var rows = await FetchAsync(BuildSymbol(s));     // HTTP 拉原始数组
                var klines = rows.SelectMany(r => ParseRow(r, s)).ToList();
                if (klines.Count > 0) await SaveAsync(klines, ct);
            }
            catch (Exception ex) { Logger.LogError(ex, $"{BuildSymbol(s)} failed"); }
        }));
    }
}
// 具体服务只 override 5 个变体成员：
// public class ApiSourceAKlineService : KlineApiServiceBase { ... }
```

**判据**：一个采集服务如果「轮询 + 分组 + 并行抓取」的骨架不变、只有 URL 和解析在变，就该抽成这样的基类。

---

## 第三部分：从零设计任何采集器的通用方法

1. **先写死一个真实源跑通**——别在没跑通前抽象，抽象要从第二个实例里长出来。
2. **并排找边界**——列出「一模一样」（生命周期/管线）和「不一样」（协议/解析/URL）。
3. **不变下沉、变体上提**——不变进基类（模板方法），变体成 abstract/策略，**签名对齐**。
4. **最易变的部分单独隔离**——报文/行列解析是最常改的，别让它和连接逻辑搅在一起。
5. **套宿主 + 开关**——`BackgroundService` 包一层接进宿主，开关配置独立启停。

**什么时候值得抽模板**：同类源 ≥ 2 个，且「不变」部分足够厚（连接/重连/缓冲/落库/推送），
抽一次省 N 次。只有一个源、或变体比不变还多时，别硬抽。

---

## 一个真实实现的对照（FXH Spider）

这套设计在 `FXH Spider`（`Pdr.Spider.Selenium` 工程）里有一个**雏形**，几处走样，重构时注意：

1. **两个源塞一个类**：`MexcGateioHandler` 用 `ServerName == "mexc"/"gateio"` 字符串分叉
   （订阅/收数据/取标的/心跳全是）。干净设计是一个源一个 `XxxHandler`。分叉越多越该拆成独立类。

2. **模板方法半接线**：`ExchangeHandlerBase.StartAsync` 只做 `GetSymbols` → `GroupSymbols` →
   `Task.Run(PublishDataAsync)`，连 WebSocket/订阅/收数据那段**被注释掉了**；`ConnectAsync`/
   `SubscribeAsync`/`ReceiveDataAsync` **没有调用者**；重连 + `Environment.Exit(1)` 也在零调用的方法里。

3. **入库/推送模型混用编译不过**：`ProcessKlineQueue` 把 `List<BinanceKline>` 传给收 `List<Kline>` 的
   `SaveMexcKline`，两个是独立类（对应决策 2）。

4. **标的缓存已按决策 1 解耦**（`CacheKlineSymbolService` 单独维护），这是设计兑现得最好的一处。

5. **REST 采集没抽基类**：`ApiMexcKlineService` / `ApiBinanceKlineService` / `ApiGateioKlineService`
   是三个各自为政的 `BackgroundService`，骨架雷同但没共享基类——这正是决策 5 的模板该落的地方。

6. **`BinanceKlineService` / `UpDownKlineService` 是「模板抽出来之前」的形态**：直接在自己
   `ExecuteAsync` 里手写连/订/收，绕过 `ExchangeHandler` 抽象，只服务单一源/单一标的。

7. **命名空间不随目录**：`GasPriceService` 在 `HostService/Data/` 下但 namespace 是 `Pdr.Spider.Selenium.HostService`；
   `ChainCatcherSpiderSelWorker.cs` 在 `Sel/Flash/` 下却 namespace 是 `FXH.SpiderAgent.Spider`。抄代码先看 `namespace`。

8. **开关配置当前是坏的**：开发环境的 `ServiceSelector` 值没有对应分支、生产环境压根没这个键 →
   跑起来零采集器。设计是好的（开关隔离），配置没跟上。

## 参考源码（FXH Spider 内的对照文件）

| 设计层 | 文件 |
|---|---|
| 契约 / 模板 / 策略 / 适配 | `MexcGateios/IExchangeHandler.cs`、`ExchangeHandlerBase.cs`、`MexcGateioHandler.cs`、`ExchangeService.cs` |
| 报文解析 / 标的 | `MexcGateios/ExhangeHelper.cs` |
| 标的缓存解耦 | `HostService/Kline/CacheKlineSymbolService.cs` |
| REST 采集（未抽基版的雏形） | `HostService/Kline/Day/ApiMexcKlineService.cs`、`ApiBinanceKlineService.cs` |
| 非模板 WS 写法 | `HostService/Kline/Day/BinanceKlineService.cs`、`HostService/Kline/UpDownKline/UpDownKlineService.cs` |
| 入库/推送模型 | `FXH.Domain/Repositories/Mongo/IMongoRepository.cs`（`Kline` vs `BinanceKline`） |
| 注册开关 | `HostedServiceCollectionExtensions.cs` |
