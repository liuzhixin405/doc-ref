# 01 · 从零搭骨架

> **代码来源约定**：本文件里的代码标 `[实现: <路径>]` 表示逐字取自本仓库 —— 这是最强的依据
> （代码在真机上跑着），比任何文档都硬。框架 API 的解释用四级标注，含义同 `dotnet-kb`：
> `[来源: id p页码]` = 本地官方 PDF 可翻页核对；`[联网-官方: URL]` = L1；`[联网-非官方: URL]` = L2；
> 无标注 = L3，未经核实。

## 目录

- [1. 解决方案分层](#1-解决方案分层)
- [2. 每个工程干什么](#2-每个工程干什么)
- [3. csproj 契约](#3-csproj-契约)
- [4. 宿主装配](#4-宿主装配)
- [5. 配置项](#5-配置项)
- [6. 启动自检](#6-启动自检)
- [7. 搭建顺序](#7-搭建顺序)

---

## 1. 解决方案分层

```
s01-erpservice/
├── CustomAttribute/            特性契约（零依赖，所有人都能引用）
├── Proj.Base/              平台层，8 个工程
│   ├── Proj.Base.Model/        实体 + DTO + 枚举
│   ├── Proj.Base.Common/       工具、App 服务定位器、UoW、元数据加载
│   ├── Proj.Base.IServices/    接口契约
│   ├── Proj.Base.Repository/   仓储
│   ├── Proj.Base.Services/     通用动作引擎（策略实现）
│   ├── Proj.Base.Reflect/      动态代理 + 业务装载器
│   ├── Proj.Base.Action/       泛型控制器 InitController<TModel>
│   └── Proj.Base.Extensions/   Autofac、中间件、授权、过滤器
├── Business/                   业务层，按域分包，编译产物指向宿主 lib/
│   ├── SYS/ MST/ SAL/ PUR/ INV/ CST/ AR/ AP/
└── Proj.Api/               宿主（启动工程）
    ├── Controllers/            LoginController 等非业务控制器
    ├── lib/                    业务模块 DLL 落地目录
    └── wwwroot/ui/            前端编译产物
```

**依赖方向是单向的**：`Business → Base → CustomAttribute`。宿主 `Proj.Api` 引用 `Base`
但不引用 `Business` —— 这是整个架构的支点，见 [SKILL.md](../SKILL.md) 决策 1。

## 2. 每个工程干什么

| 工程 | 放什么 | 典型文件 |
|---|---|---|
| `CustomAttribute` | 只有三个空特性类 + `ICusAop`，**不引用任何东西** | `CusActionAtttibute.cs`、`CusTransAttribute.cs` |
| `Base.Model` | `[SugarTable]` 实体、DTO、枚举 | `T_META_DataFormMst.cs`、`SysSingleTableDTO.cs` |
| `Base.Common` | `App` 静态定位器、`DevelopHelper`、`UnitOfWorkManage`、Helper | `App.cs` |
| `Base.IServices` | `IBusiness<T>`、`IBusinessAop`、`IBaseOperateServices<T>` | `IBusiness/IBusinessAop.cs` |
| `Base.Repository` | `BaseRepository<T>`、`FormCommonRepository` | `BaseRepository.cs` |
| `Base.Services` | `BaseOperateServices<T>` 门面 + 10 个动作策略实现 | `BaseServices/Implement/` |
| `Base.Reflect` | `BusinessProvider`、`CusProxyGeneratorNew<T>` | `CusProxyGenerator.cs` |
| `Base.Action` | `InitController<TModel>`（**整个工程只有这一个文件**） | `InitController.cs` |
| `Base.Extensions` | `AutofacModuleRegister`、`ByPassAuthMiddleware`、`PermissionHandler` | — |

`CustomAttribute` 必须零依赖，因为它是**宿主和业务模块唯一共享的契约**。一旦它引用了别的工程，
那个工程就被拖进了这条最小依赖链。

## 3. csproj 契约

**宿主** `[实现: Proj.Api/Proj.Api.csproj]`：

```xml
<ItemGroup>
  <ProjectReference Include="..\CustomAttribute\CustomAttribute.csproj" />
  <ProjectReference Include="..\Proj.Base\Proj.Base.Action\Proj.Base.Action.csproj" />
  <ProjectReference Include="..\Proj.Base\Proj.Base.BusinessCommon\Proj.Base.BusinessCommon.csproj" />
  <ProjectReference Include="..\Proj.Base\Proj.Base.Common\Proj.Base.Common.csproj" />
  <ProjectReference Include="..\Proj.Base\Proj.Base.Extensions\Proj.Base.Extensions.csproj" />
  <!-- 需要什么加什么（消息推送、文件存储、报表……），但永远不要加 Business/* -->
</ItemGroup>
```

**这份清单的长度不重要，重要的只有一件事：里面没有任何 `Business/*`。**
宿主需要哪些平台能力（消息推送、云存储、报表导出）是自由选择，加上去不影响架构；
一旦引用了任何一个业务模块，编译期解耦就破了，整套插件机制失去意义。

**业务模块** `[实现: Business/SAL/MOD001_Sample/MOD001_Sample.Services/MOD001_Sample.Services.csproj]`：

```xml
<ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.Action\Proj.Base.Action.csproj" />
<ProjectReference Include="..\MOD001_Sample.Business\MOD001_Sample.Business.csproj" />
<ProjectReference Include="..\MOD001_Sample.Model\MOD001_Sample.Model.csproj" />
<ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.Services\Proj.Base.Services.csproj" />
<ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.IServices\Proj.Base.IServices.csproj" />
<ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.Reflect\Proj.Base.Reflect.csproj" />

<AppendTargetFrameworkToOutputPath>output</AppendTargetFrameworkToOutputPath>
<OutputPath>..\..\..\..\Proj.Api\lib</OutputPath>
```

两行**缺一不可**：
- `AppendTargetFrameworkToOutputPath=true` —— 不追加 `net8.0` 子目录
- `OutputPath` 指向宿主 `lib/`

少了任何一个，DLL 就落不到 `lib/`，运行时 `BusinessProvider` 抛
`FileNotFoundException`。这个错误信息只告诉你「找不到文件」，不会告诉你「csproj 写错了」。

## 4. 宿主装配

`[实现: Proj.Api/Program.cs]` 的骨架：

```csharp
var builder = WebApplication.CreateBuilder(args);

// ① 业务程序集：从 lib/ 按 ServiceList 白名单动态装载
builder.AddControllers().ConfigureApplicationPartManager(apm =>
{
    var folder = Path.Combine(Directory.GetCurrentDirectory(), "lib");
    var serviceList = (builder.Configuration.GetSection("ServiceList").Get<string[]>())
                      ?? new string[] { "SYS", "MST" };
    string[] serviceFiles = Directory.GetFiles(folder, "*.Services.dll")
        .Where(x => serviceList.Any(y => x.Contains(y))).ToArray();
    foreach (var file in serviceFiles)
    {
        var assembly = Assembly.LoadFrom(file);
        apm.ApplicationParts.Add(new AssemblyPart(assembly));
    }
});

// ② Autofac
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(b => b.RegisterModule(new AutofacModuleRegister()));

// ③ 热重载守护
builder.Services.AddHostedService<LibFileWatcherService>();

var app = builder.Build();
App.RootServices = app.Services;   // 静态定位器拿到根容器，必须在 Build 之后
app.Run();
```

**`AssemblyPart` 的正确用法**在本地库里有明确记载
`[来源: aspnet-core-aspnetcore-10.0#0519 p4288]`：

```csharp
// This creates an AssemblyPart, but does not create any related parts for items such as views.
var part = new AssemblyPart(assembly);
services.AddControllersWithViews()
    .ConfigureApplicationPartManager(apm => apm.ApplicationParts.Add(part));
```

注意文档里的措辞 —— `AssemblyPart` **只产出 controller 这类类型，不产出 Razor 视图等关联部件**。
本架构没有 Razor 页面，所以正好够用；如果将来要加，得换成 `CompiledRazorAssemblyPart`。

`Assembly.LoadFrom` 在本场景下的取舍 `[联网-官方: https://learn.microsoft.com/dotnet/api/system.reflection.assembly.loadfrom]`：

- 官方**推荐用 `AssemblyLoadContext` 的重载替代** `Assembly.LoadFrom`
- 它把程序集装进默认 `AssemblyLoadContext`，并挂一个 `AppDomain.AssemblyResolve`
  处理器，从该程序集所在目录解析它的依赖 —— 这正是本架构能工作（业务 DLL 和它的依赖同在
  `lib/`）的原因
- 代价：**装进去就出不来**。同一 identity 已加载时 `LoadFrom` 直接返回已加载的那个，
  即使路径不同

## 5. 配置项

`[实现: Proj.Api/appsettings.json]`：

```jsonc
{
  "ServiceList": ["SYS","MST","CST","SAL","INV","PUR","AR","AP"],
  "MainDB": "Proj_System",
  "MutiDBEnabled": true,
  "DBS": [
    { "ConnId": "Log",         "DBType": "SqlServer", "ConnectionString": "..." },
    { "ConnId": "Proj_System", "DBType": "SqlServer", "ConnectionString": "..." }
  ],
  "SeedDBEnabled": false,
  "isUseAuth": true
}
```

| 配置 | 作用 |
|---|---|
| `ServiceList` | **模块白名单**。`{域}.Services.dll` 的文件名包含其中任一字符串才装载 |
| `MainDB` | 系统库连接名 —— 元数据、账套注册表、权限表都在这 |
| `MutiDBEnabled` | 多账套开关 |
| `SeedDBEnabled` | 启动时是否 Code First 建表 + 灌种子数据 |
| `isUseAuth` | 权限校验总开关 |

⚠️ 这类系统的 `appsettings.json` 里通常**直接硬编码着数据库 SA 密码、第三方服务密钥、
云存储连接串**。搭的时候第一件事就是把这些挪进环境变量或密钥管理 ——
一旦提交过，光删文件没用，**轮换密钥比删文件更实际**。

## 6. 启动自检

这套架构最容易出的问题是**静默失效**，所以值得在启动时主动失败一次。放在 `Program.cs`
`app.Run()` 之前：

```csharp
// 启动自检：ServiceList 里声明了但 lib/ 里没有的模块，直接启动失败
var libDir = Path.Combine(Directory.GetCurrentDirectory(), "lib");
var declared = builder.Configuration.GetSection("ServiceList").Get<string[]>() ?? Array.Empty<string>();
var present  = Directory.Exists(libDir)
    ? Directory.GetFiles(libDir, "*.Services.dll")
    : Array.Empty<string>();
var missing = declared.Where(d => !present.Any(f => f.Contains(d))).ToArray();
if (missing.Length > 0)
{
    throw new InvalidOperationException(
        $"ServiceList 声明了 {string.Join(",", missing)}，但 lib/ 下没有对应的 *.Services.dll。" +
        $"检查这几个模块的 csproj 是否设了 OutputPath 指向 Proj.Api/lib。");
}
```

**为什么值得加**：`BusinessProvider` 是在**控制器构造时**才去 `lib/` 找 DLL 的。
没有自检的话，「漏编译一个模块」的表现是「上线后点到那个菜单才 500」，
而不是「启动就挂」。启动自检把发现时间从「用户点开」提前到「部署时」。

同理，`DevelopHelper.GetDataFormMst` 查不到元数据时返回 `null`，症状是「新增保存后主表没数据」
而不是报错 —— 也可以考虑在自检里扫一遍所有 FormCode。

## 7. 搭建顺序

按这个顺序，每步都能编译：

1. `CustomAttribute`（3 个特性 + `ICusAop`）
2. `Base.Model`（先只放元数据三表实体 + `OperateEntity`）
3. `Base.IServices`（`IBusiness<T>`、`IBusinessBase<T>`、`IBusinessAop`、`IBaseOperateServices<T>`）
4. `Base.Common`（`App`、`UnitOfWorkManage`、`DevelopHelper`）
5. `Base.Repository`（`BaseRepository<T>`）
6. `Base.Services`（`BaseOperateServices<T>` + 先只实现 Add/Update/Query）
7. `Base.Reflect`（`BusinessProvider` + `CusProxyGeneratorNew<T>`）
8. `Base.Action`（`InitController<TModel>`）
9. `Base.Extensions`（`AutofacModuleRegister` + 中间件）
10. `Proj.Api`（`Program.cs` + `appsettings.json`）
11. 一个最小业务模块

**第 6 步先只做 Add/Update/Query 三个动作。** 把 10 个动作全做完再联调，
一旦跑不通你会在 10 个地方同时怀疑自己。三个动作足以验证整条链路：
元数据加载 → 策略分发 → 事务 → 动态代理 → 控制器。

继续读 [02-plugin-and-aop.md](02-plugin-and-aop.md)。
