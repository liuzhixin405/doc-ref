# 05 · 数据访问、多账套与事务

> 依据标注同 [01](01-scaffold.md)：`[实现: 路径]` = 取自本仓库；`[来源: id p页码]` = 本地官方 PDF；
> `[联网-官方: URL]` = L1；**「分析」= 我的推断，不是文档结论**。

## 目录

- [1. 多账套模型](#1-多账套模型)
- [2. 切库入口 GetCurrentDb](#2-切库入口-getcurrentdb)
- [3. App.User：全局用户缓存](#3-appuser全局用户缓存)
- [4. 仓储的两种连接](#4-仓储的两种连接)
- [5. 事务](#5-事务)
- [6. 认证与鉴权](#6-认证与鉴权)
- [7. 复刻清单](#7-复刻清单)

---

## 1. 多账套模型

**一个账套 = 一个物理数据库。**

```
Proj_System（主库）
├── T_Sys_Account      账套连接注册表：fMandt + fAccountID → 服务器/库名/账号/密码
├── T_META_Data*       元数据
├── T_Sys_Function_Library / T_Sys_FunRights / T_Sys_Process   权限与审批流配置
└── t_Sys_Receipt*     单据编号规则

{客户A}_1001（账套库）
├── T_Sample_Master
└── T_Sample_Item
```

`[实现: Proj.Base/Proj.Base.Model/Models/Proj_System/T_Sys_Account.cs]`：

```csharp
[SugarTable("T_Sys_Account", "Proj")]
public class T_Sys_Account
{
    [SugarColumn(IsPrimaryKey = true)] public string fMandt { get; set; }      // 集团
    [SugarColumn(IsPrimaryKey = true)] public int fAccountID { get; set; }    // 账套号
    public int? fSeqNo { get; set; }
    public string fServer { get; set; }     // 数据库服务器
    public string fDBCode { get; set; }
    public string fDBName { get; set; }     // 物理库名
    public string fDBUser { get; set; }
    public string fDBPSW { get; set; }
}
```

**为什么用「一账套一库」而不是「一个库加 TenantId 列」**：
- 数据隔离是物理的 —— 客户要备份/迁移/删除自己的数据，`BACKUP DATABASE` 一句就够
- 客户要单独调优、单独加索引、单独放到另一台服务器，都不用改代码
- 避免了「任何一条查询漏写 `WHERE TenantId=...`」这类越权

**代价**：跨账套统计要做 ETL；连接数随账套数线性增长；表结构变更要跑 N 个库。

`fMandt` 是关键 —— 它是**集团**维度，同一个集团下可以有多个账套（不同年度/不同公司）。
所以 `(fMandt, fAccountID)` 才是完整的租户标识，两处都不能漏。

## 2. 切库入口 GetCurrentDb

`[实现: Proj.Base/Proj.Base.Common/App.cs:114-140]`：

```csharp
public static ISqlSugarClient GetCurrentDb()
{
    var scope = App.GetService<ISqlSugarClient>();
    ISqlSugarClient db = scope as SqlSugarScope;

    if (App.User is { fAccountID: > 0 })
    {
        var tenant = db.Queryable<T_Sys_Account>().With(SqlWith.NoLock).WithCache()
            .Where(s => s.fAccountID == App.User.fAccountID && s.fMandt == App.User.fMandt)
            .First();

        if (tenant != null)
        {
            var iTenant = db.AsTenant();
            if (!iTenant.IsAnyConnection(tenant.fAccountID + "_" + tenant.fMandt))
            {
                iTenant.AddConnection(tenant.GetConnectionConfig());
            }
            db = iTenant.GetConnectionScope(tenant.fAccountID + "_" + tenant.fMandt);
        }
    }
    return db;
}

public static ISqlSugarClient GetCurrentNoTrxDb() => GetCurrentDb().CopyNew();

public static ISqlSugarClient GetSystemDb()
    => GetService<ISqlSugarClient>().AsTenant().GetConnection("proj_system");
```

四个要点：

**① 连接是按需注册的。** 第一次访问某账套时 `AddConnection`，之后 `IsAnyConnection` 命中直接复用。
连接配置来自 `T_Sys_Account` 行 —— 所以**加一个新账套只需要往这张表插一行**，不用改配置文件。

**② 每个账套一次查库。** `GetCurrentDb()` 每次调用都要查一次 `T_Sys_Account`。
`WithCache()` 是 SqlSugar 的二级缓存，实际开销很小，但**它不参与事务**（`.With(SqlWith.NoLock)`）。

**③ `GetCurrentNoTrxDb()` = `CopyNew()`。** SqlSugar 的 `CopyNew()` 返回一个独立的客户端实例，
**不共享当前事务**。什么时候用哪个：

| 场景 | 用哪个 | 为什么 |
|---|---|---|
| 增删改（要回滚的） | `GetCurrentDb()` | 必须在事务里 |
| 只读校验（审核前查库存等） | `GetCurrentNoTrxDb()` | 见下 |
| 写日志 / 发消息 | `GetCurrentNoTrxDb()` | 业务回滚了日志也该留 |

**为什么只读校验要用 NoTrx**：用 `GetCurrentDb()` 的话，读操作会挂在当前事务上。
如果外层因为别的原因回滚，你读到的数据状态和最终落库的状态不一致 ——
更糟的是长事务里读会持锁，容易死锁。**这是一个容易忽略但很重要的纪律**：
`UnAudit` 里「检查是否已被下游引用」这类校验，用错连接会导致偶发的错误拦截。

**④ `GetSystemDb()` 固定回主库。** 读元数据、权限、编号规则时用它，
因为这些表**只在主库**，不在账套库。

## 3. App.User：全局用户缓存

`[实现: Proj.Base/Proj.Base.Common/App.cs:39-77]`：

```csharp
private static IUser cacheUser;

public static IUser User
{
    get
    {
        _semaphore.Wait();
        try
        {
            if (cacheUser != null && cacheUser.fUserID != 0 && !IsRefreshUser)
            {
                IsRefreshUser = false;
                return cacheUser;
            }
            else
            {
                var user = RootServices?.CreateScope().ServiceProvider.GetService<IUser>();
                if (user != null && user.fUserID != 0)
                {
                    cacheUser = user;
                    return cacheUser;
                }
                else
                    return user;
            }
        }
        finally { _semaphore.Release(); }
    }
}

public static bool IsRefreshUser = false;
```

### 这是整套架构里最需要小心的一块

**设计意图**：`IUser` 的实现（`AspNetUser`）要从 `IHttpContextAccessor` 解 JWT claims，
每次访问都解析一遍成本高。于是加了一层**进程级静态缓存**，只有 `IsRefreshUser = true` 时才重读。

**自愈机制**：`PermissionHandler` 每次授权时比对 JWT 里的 `fUserCode` 和缓存的：

```csharp
if (httpContext.User.Claims.SingleOrDefault(s => s.Type == "fUserCode")?.Value != App.User.fUserCode)
{
    App.IsRefreshUser = true;      // 上面这次 App.User 读到的可能是别人的
}
if (App.User.fUserType != "99")    // 再读一次 —— 这次刷新了
{
    var isMatchRole = await _funRights.CanRunURL(questUrl, App.User.fUserCode);
    ...
}
```
`[实现: Proj.Base/Proj.Base.Extensions/Authorizations/Policys/PermissionHandler.cs:115-122]`

**所以要理解这个流程**：请求 A（用户甲）→ 缓存甲；请求 B（用户乙）→ `App.User` 先返回**甲**，
PermissionHandler 发现不符 → 置 `IsRefreshUser` → 第二次读拿到乙。

### 三个必须知道的风险

**① 正确性依赖权限处理器。** 自愈发生在 `PermissionHandler` 里。如果：
- `isUseAuth = false`（关了权限过滤）
- `AppSettings:UseLoadTest = true`（测试模式）

那么 `App.User.fUserType != "99"` 的分支才走权限……但**刷新那行在分支外面，仍然会执行**。
不过如果整个 handler 因为其他原因没跑（比如端点没标 `[Authorize]`），
`App.User` 就没有自愈机会，会返回上一个请求的用户 ——
**而 `GetCurrentDb()` 正是用 `App.User.fAccountID` 来切库的**。后果是**操作了错误的账套**。

**② 全局信号量是吞吐瓶颈。** `_semaphore.Wait()` 在每个 `App.User` 访问时都要拿锁。
`App.User` 在一次请求里被访问很多次（`GetCurrentDb`、`DevelopHelper`、`PermissionHandler`、
业务代码……）。**这意味着同一时刻只有一个请求能读到用户信息**，其余全部排队。

分析：这是一处典型的「为了线程安全牺牲了并发」。正确做法是**用 `IHttpContextAccessor` 或
`AsyncLocal<T>` 做请求级缓存**，而不是进程级静态字段加锁。

**③ 静态缓存 + 多租户 = 泄漏面。** 任何进静态字段的请求相关状态，
都会跨请求、跨用户、跨账套存活。`DevelopHelper.Db`（见 [03 §3](03-metadata.md#3-加载与缓存)）
是同一类问题的另一个实例。

### 文档怎么说

官方文档在 `HttpContext` 这一节明确写了
`[来源: aspnet-core-aspnetcore-10.0#0244 p1549-1563]`：

> The `HttpContext` is **NOT** thread safe. Accessing it from multiple threads can result in
> unpredictable results, such as exceptions and data corruption. The `IHttpContextAccessor` …

即：**框架层面不保证 `HttpContext` 的线程安全，官方推荐用 `IHttpContextAccessor` 拿到之后
只在当前请求内使用，不要跨线程持有。** `App.HttpContext` 这个静态属性正是跨线程持有的形态：

```csharp
public static HttpContext HttpContext => _httpContextAccessor?.HttpContext ?? RootServices?.GetService<HttpContext>();
```

它自己加了 `??` 兜底（`GetService<HttpContext>()` 在非请求线程上通常返回 null），
说明作者已经遇到过「拿不到」的情况。**复刻时把 `App.User` / `App.HttpContext` 改成
请求作用域（scoped）依赖注入**，能一次性消掉上面三个风险。

## 4. 仓储的两种连接

`[实现: Proj.Base/Proj.Base.Repository/BASE/BaseRepository.cs:24-56]`：

```csharp
private ISqlSugarClient _db
{
    get
    {
        ISqlSugarClient db = _dbBase;
        if (App.User is { fAccountID: > 0 })
        {
            var tenant = db.Queryable<T_Sys_Account>().With(SqlWith.NoLock).WithCache()
                .Where(s => s.fAccountID == App.User.fAccountID && s.fMandt == App.User.fMandt).First();
            if (tenant != null)
            {
                var iTenant = App.GetService<IUnitOfWorkManage>().GetDbClient();
                if (!iTenant.IsAnyConnection(tenant.fAccountID + "_" + tenant.fMandt))
                    iTenant.AddConnection(tenant.GetConnectionConfig());
                db = iTenant.GetConnectionScope(tenant.fAccountID + "_" + tenant.fMandt);
            }
        }
        db.Ado.IsEnableLogEvent = bool.TryParse(_config["IsEnableLogEvent"], out var result) && result;
        return db;
    }
}
```

和 `App.GetCurrentDb()` **做的是同一件事**，差别在切库那一句走的路径：
`App.GetCurrentDb()` 从 `App.GetService<ISqlSugarClient>()` 转 `SqlSugarScope`；
仓储从 `UnitOfWorkManage.GetDbClient()` 拿 `AsTenant()`。

两者最终指向同一个 SqlSugar 租户实例，所以**在事务里的写入是同一个连接**——这是必须的。
但两条路径并存意味着：**改切库逻辑要同时改两处**，漏一处就会出现「App 层换了库、仓储还在旧库」
这种极难查的问题。复刻时抽成一个共用方法。

仓储同时暴露 `_dbWithoutTrx`（`_db.CopyNew()`），对应上面的 `GetCurrentNoTrxDb()`。

## 5. 事务

`[实现: Proj.Base/Proj.Base.Common/UnitOfWork/UnitOfWorkManage.cs]`：

```csharp
public class UnitOfWorkManage : IUnitOfWorkManage
{
    private int _tranCount { get; set; }
    public int TranCount => _tranCount;
    public readonly ConcurrentStack<string> TranStack = new();

    public ITenant GetDbClient() => _sqlSugarClient.AsTenant();   // 必须 AsTenant，后面要切库

    public void BeginTran()
    {
        lock (this) { _tranCount++; GetDbClient().BeginTran(); }
    }

    public void CommitTran()
    {
        lock (this)
        {
            _tranCount--;
            if (_tranCount == 0)
            {
                try { GetDbClient().CommitTran(); }
                catch (Exception ex) { Log.Error(ex.ToString()); GetDbClient().RollbackTran(); }
            }
        }
    }

    public void RollbackTran()
    {
        lock (this) { _tranCount--; GetDbClient().RollbackTran(); }
    }

    public void CloseConnection()
    {
        lock (this) { _tranCount = 0; GetDbClient().Close(); }
    }
}
```

### 嵌套语义有一个真实的缺陷

分析，按代码逐行推演：

```
外层 BeginTran          _tranCount: 0 → 1,  DB.BeginTran()
  内层（Count>0，跳过 BeginTran）
  内层 CommitTran       _tranCount: 1 → 0,  _tranCount == 0 → DB.CommitTran()   ← 提交了整个事务！
外层 CommitTran         _tranCount: 0 → -1, 不为 0，不再提交
finally CloseConnection _tranCount = 0
```

**内层提交会提前提交整个外层事务。** 如果内层之后外层还有操作失败了，
那部分操作不在事务里，回滚不掉。

复刻时改成**只有嵌套层级为 0 的那一次 `BeginTran` 对应唯一一次 `CommitTran`**：

```csharp
public void BeginTran()
{
    lock (this)
    {
        // 只在最外层真正开事务 —— 这一点现有代码是对的
        if (_tranCount == 0) GetDbClient().BeginTran();
        _tranCount++;
    }
}

public void CommitTran()
{
    lock (this)
    {
        _tranCount--;
        if (_tranCount == 0) GetDbClient().CommitTran();   // 只有归零才提交
    }
}

public void RollbackTran()
{
    lock (this)
    {
        // 回滚就是回滚，直接把计数清零，避免后续误提交
        _tranCount = 0;
        GetDbClient().RollbackTran();
    }
}
```

`TranStack` / `BeginTran(MethodInfo)` / `CommitTran(MethodInfo)` 那一套是**早期 Based on
方法名的嵌套实现**，当前没有任何调用方（`Trans` 走的是无参重载）。可以整段删掉。
里面那个 `while (!TranStack.TryPeek(out result)) Thread.Sleep(1);` 是忙等，更要删。

### 事务的边界在哪

回顾 [02 §5](02-plugin-and-aop.md#5-事务边界)：事务开在**代理的 `Trans`** 里，
覆盖**一个业务动作的完整执行**（`Add` 或 `Update` 或 `Audit`……）。
所以「一个动作 = 一个事务」—— 主表和所有明细要么全成功要么全回滚。

**跨动作的事务是不存在的。** 不存在「先审核再自动过账，两步一起回滚」这种事。
要保证这种一致性，得在业务方法内部调两次 `_businessBase`，靠 `TranCount` 的嵌套计数包住 ——
但那就踩到上面的嵌套缺陷了。

## 6. 认证与鉴权

链路：

```
请求 → ByPassAuthMiddleware
        解 JWT，构造 ClaimsIdentity，塞进 context.User
        claims: ID / fMandt / fUserType / fAccountID / fUserCode / fUserID / fUserName
     → [Authorize(Policy = Permissions.Name)]
     → PermissionHandler
        ① UseLoadTest 开 → 跳过
        ② 自愈 App.User（见 §3）
        ③ fUserType == "99"（超管）→ 放行
        ④ CanRunURL(url, fUserCode) → 查 T_Sys_FunRights
        ⑤ token 过期 → 401
        ⑥ CheckUserPwd → 402 提示改密码
     → InitController 动作
```

**权限是 URL 级的**，配置在 `T_Sys_Function_Library`（功能清单）+ `T_Sys_FunRights`（角色-功能映射）。
这意味着**加一个模块必须同时配权限**，否则新接口默认没人能访问 ——
这也是为什么「加完模块要点不出来」是个高频问题。

**字段级权限**是另一套：`T_META_DataInterFields.fIfRoleApply` 标了哪些字段参与角色权限控制。

## 7. 复刻清单

从零搭这套数据层时，按这个顺序做，每一条都对应上面某个坑：

1. `T_Sys_Account` 建表 + 提供一个「新增账套」的管理界面
2. `App.GetCurrentDb()` 用 SqlSugar 的 `AsTenant` / `AddConnection` / `GetConnectionScope` 三件套
3. **把 `App.User` 改成请求作用域注入**，不要用静态字段 + 信号量
4. **仓储和 `App` 层的切库逻辑抽成一个共用方法**
5. `UnitOfWorkManage` 只保留 `BeginTran()` / `CommitTran()` / `RollbackTran()` / `CloseConnection()`，
   按 §5 修正嵌套语义，其余删掉
6. 明确约定「只读校验走 NoTrx，写走 Trx」，写进团队规范
7. `IsEnableLogEvent` 这类开关走配置，**不要把连接串和密码写进 `appsettings.json`**

继续读 [06-module-template.md](06-module-template.md)。
