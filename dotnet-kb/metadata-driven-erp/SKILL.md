---
name: metadata-driven-erp
description: Use when building or extending a metadata-driven, plugin-based ERP / line-of-business backend in .NET — the pattern where the host never compiles against business modules, business DLLs are loaded from lib/ at runtime, document CRUD is driven by form-metadata config tables instead of hand-written code, and per-action AOP + transactions are woven with DispatchProxy. Triggers whenever the user mentions 元数据驱动、通用CRUD引擎、通用增删改查、业务模块四件套、动态加载DLL、插件化模块、DispatchProxy AOP、CusAction/CusTrans、主从表单据、单据状态机、多账套、FormCode, or asks to scaffold such a solution or add a business module to one — even if they never say "skill" or name the architecture.
---

# 元数据驱动 + 插件化的 ERP 后端架构

这套架构的全部价值在于**把「重复」抽干**。一个企业 ERP 有几百个单据界面，每个界面的增删改查、
字段校验、权限控制、单据编号、日志、消息通知都长得差不多。与其写几百遍，不如：

> 用**配置表**描述每个界面长什么样，用**一套通用引擎**执行它，业务代码只写那些真正特殊的逻辑。

结果是一套上百个模块的系统里，绝大多数业务类只有 5~10 行有效代码。

## 先说清楚：下面出现的名字都是占位符

这套 skill 用一组**中性的占位名**描述架构，不带任何具体项目的身份。照着它搭的时候，
把占位名换成你自己的即可：

| 占位名 | 指什么 | 换成 |
|---|---|---|
| `Proj` / `Proj.Api` | 产品前缀 / 宿主（启动）工程 | 你的公司或产品简称 |
| `Proj.Base.*` | 平台层各工程（见 [01](references/01-scaffold.md)） | — |
| `MOD001_Sample` | 业务模块号 | `{域}{3位序号}_{名称}` |
| `T_Sample_Master` / `T_Sample_Item` | 示例的主表 / 明细表实体 | 你的表名 |
| `T_META_Data*` | 元数据三张配置表 | — |
| `Proj_System` | 主库（系统库） | — |
| `SYS / MST / SAL / PUR / INV / CST / AR / AP` | 域前缀（系统/基础/销售/采购/库存/成本/应收/应付） | — |

**保留不换的**是框架自身的词汇：`f` 开头的字段名、`Mst` / `lstItem1..N`、`fMandt`、`T_Sys_*`、
动作名（`Add` / `Audit` / `Submit`…）。**这些不是命名风格，是引擎的契约** ——
`fDtoVar` 要拿 `Mst` 去 `GetProperty`，DTO 类名去掉 `DTO` 要等于 DLL 文件名。
改了引擎就不工作。

下文标 `[实现: …]` 的代码，都取自一套**在用的真实实现**，只把标识符换成了占位名。
它能直接编译，不是伪代码。

## 先判断你走哪条路

| 用户想要 | 走哪条 | 读什么 |
|---|---|---|
| 从空目录搭出这套分层解决方案 | **路径 A** | [references/01-scaffold.md](references/01-scaffold.md) |
| 在已有项目里加一个单据模块 | **路径 B** | [references/06-module-template.md](references/06-module-template.md) |
| 搞清楚某个机制怎么实现 | 查下表 | 对应 reference |

无论走哪条，先把下面五条设计决策理解了。它们是这套架构的钢筋，违反了整个结构就会垮。

---

## 五条不可动摇的设计决策

### 1. 宿主不认识任何业务模块

`Proj.Api.csproj` 的 ProjectReference 里**一个 `Business/*` 都没有**。业务模块反过来把编译输出
指向宿主的 `lib/` 目录：

```xml
<OutputPath>..\..\..\..\Proj.Api\lib</OutputPath>
```

**为什么这样做**：编译期解耦让模块可以独立开发、独立编译、按需裁剪。宿主靠 `appsettings.json` 的
`ServiceList` 白名单决定装载哪些模块 —— 想给某个客户砍掉整条业务线，改配置即可，不用改代码重新发布。

**代价**：你拿不到编译器的保护。改一个 DTO 名字不会报错，但运行时会静默失效（见「坑」第 4 条）。

### 2. 元数据存在数据库里，不是代码里

三张表描述一个「功能界面」：

```
T_META_DataFormMst       表单主配置（FormCode、是否自动编码、关联的审批流…）
  └─ T_META_DataInterMst      接口 = 一张表（Mst=主表 / lstItem1=明细1 / fUILevel 决定层级）
       └─ T_META_DataInterFields  字段（主键？逻辑主键？可写回？渲染成什么控件？开窗取数？）
```

运行时 `DevelopHelper.GetDataFormMst(FormCode)` 把三者拼成一棵对象树，缓存到 Redis 48 小时。

**为什么**：加一个字段、改一个校验、换一种控件，都不用改代码、不用重新发布 —— 这是 ERP 交付
现场最刚需的能力。客户说「这个字段要必输」，改一行配置就好。

### 3. 业务类只写「例外」

模块的业务类实现 `IBusiness<TDto>`，绝大多数方法**一行委托**：

```csharp
public async Task<ApiResult> Add(Dto entity) => await _businessBase.Add(entity);
```

只有真正有业务含义的动作才写代码。判断标准：**「这个规则能配出来吗？」** 能配就别写。

### 4. AOP 在运行时织入，不在编译期

用 `DispatchProxy` 包一层动态代理，代理在 `Invoke` 里读方法上的 `[CusAction]` 特性，
按声明顺序执行对应的 AOP 类，然后开事务调真实业务。

**为什么不用编译期织入（如 PostSharp/Metalama）**：业务模块是运行时才加载的 DLL，
编译期织入要求织入器在业务模块编译时就介入，和「宿主不认识业务模块」这条冲突。

**代价**：AOP 类必须和业务类在**同一个 DLL** 里 —— 代理是从这个 DLL 反射出全部 `IBusinessAop` 实现的。

### 5. 单据状态是一组「动作」，不是一个状态字段

引擎提供固定动作集，业务按需挂 AOP：

```
Add  Update  Delete | Submit UnSubmit | Audit UnAudit | Post UnPost | Close UnClose | Enable UnEnable | Import Export
```

每个动作在 `InitController` 里被拆成 `XxxBefore → business.Xxx → XxxAfter` 三段。
「动作前/动作中/动作后」的细粒度由 `ActionTime` 枚举 + `BaseOperate` 的四个钩子表达。

**为什么**：ERP 单据的生命周期是「提交→审核→过账→关闭」这种线性流转，用动作建模比用状态机字段
更贴近业务语言，也让权限、日志、消息能统一挂在动作上。

---

## 路径 A：从零搭骨架

详细步骤见 [references/01-scaffold.md](references/01-scaffold.md)。顺序不能乱：

1. **建解决方案分层** —— `CustomAttribute` / `Proj.Base.*`（8 个工程）/ `Host`
2. **写 AOP 特性契约** —— `CusActionAttribute` / `CusTransAttribute` / `CusNoTransAttribute`
3. **写接口层** —— `IBusiness<T>` / `IBusinessBase<T>` / `IBusinessAop` / `IBaseOperateServices<T>`
4. **写元数据引擎** —— 三张元数据表实体 + `DevelopHelper`
5. **写通用动作引擎** —— `BaseOperateServices<T>` 门面 + 各动作策略实现
6. **写动态装载** —— `BusinessProvider` + `CusProxyGeneratorNew<T>`
7. **写泛型控制器** —— `InitController<TModel>`
8. **宿主装配** —— Autofac + `ApplicationPartManager` 扫描 lib + 启动自检
9. **跑通一个最小单据** —— 用 [references/06-module-template.md](references/06-module-template.md) 的模板验证

**第 9 步不能省。** 这套架构的耦合点全在运行时反射上，接口写完了不代表能跑起来。
最早发现问题的办法就是立刻造一个两级单据走一遍 Add/Audit/CommonQuery。

---

## 路径 B：新增业务模块

每个模块固定四件套。完整模板与逐行说明见
[references/06-module-template.md](references/06-module-template.md)。

```
Business/{域}/{模块号}_{名称}/
├── {模块}.Model/       DTO（Mst + lstItem1）+ T_ 实体（[SugarTable] + 继承 OperateEntity）
├── {模块}.Business/    {模块}Business : IBusiness<DTO>      ← 只写"例外"
│   └── Actions/        CheckAddAop / UpdateAop : BusinessBaseAop
└── {模块}.Services/    {模块}Controller : InitController<DTO>  ← 空壳
```

三个必须对齐的字符串（对不齐就静默失效，这是最高频的坑）：

| 地方 | 值 | 由谁推导 |
|---|---|---|
| DTO 类名 | `MOD001_SampleDTO` | 手写 |
| Business DLL 名 | `MOD001_Sample.Business.dll` | `typeof(DTO).Name.Replace("DTO","")` |
| FormCode / 元数据主键 | `MOD001_Sample` | `Regex.Replace(typeof(DTO).Name, "DTO$", "")` |

**加完模块必须做的三件事**：
1. 把 `{模块}.Services.csproj` 的 `OutputPath` 指向宿主的 `lib/`
2. 确认 `ServiceList` 里有这个域的前缀（如 `SAL`）
3. 重新编译 —— 让 DLL 落到 `lib/`

---

## 读什么

| 想搞清楚 | 读 |
|---|---|
| 工程怎么分层、csproj 怎么写、宿主怎么装配 | [references/01-scaffold.md](references/01-scaffold.md) |
| DLL 怎么被装载、热重载、DispatchProxy 代理怎么织 AOP 和事务 | [references/02-plugin-and-aop.md](references/02-plugin-and-aop.md) |
| 元数据三表结构、加载与缓存、配置怎么变成 SQL | [references/03-metadata.md](references/03-metadata.md) |
| 通用增删改查引擎怎么按元数据执行 | [references/04-operate-engine.md](references/04-operate-engine.md) |
| 仓储、多账套切库、事务、审计、权限 | [references/05-data-and-tenant.md](references/05-data-and-tenant.md) |
| 新建一个模块的完整代码模板 | [references/06-module-template.md](references/06-module-template.md) |

---

## 坑清单（复刻时最容易踩的）

按踩中概率排序。前四条会**静默失效**——不报错，但功能不对，最难查。

1. **`CusTrans` / `CusNoTrans` 特性实际不生效**。代理里事务判定被硬编码成：
   ```csharp
   UseTrans = (methodKey == "CommonQuery" || methodKey == "Export") ? false : true;
   ```
   特性被读了但没用上。复刻时**要么真的实现它，要么删掉**，别留着一个会误导人的装饰。

2. **`Assembly.LoadFrom` 出现在三处，全都没有卸载路径**：`BusinessProvider.GetBusinessNew`、
   `FormCodeHelper.GetInitConfig`、宿主启动扫描。`ApplicationPartManager` 持有的程序集永久驻留。
   复刻时若模块多，考虑加 `AssemblyLoadContext` 做可卸载的隔离。

3. **元数据有 DB 和代码两个来源，且可以不一致**。`DevelopHelper.GetDataFormMst` 读 DB 表；
   `FormCodeHelper.GetInitConfig` 反射读 `{FormCode}.Services.FormConfig.InitFormConfig()`——
   一份编译进 DLL 的硬编码元数据。后者只在开窗/下拉（`GetDataSourceSql`）拿不到 DB 元数据时兜底。
   两份真身没有一致性校验，排查「为什么开窗显示的字段和编辑页不一样」会很痛苦。

4. **`FormCode` 由类型名推导**。DTO 改名即静默失效，因为 `typeof(T).Name.Replace("DTO","")`
   同时决定了元数据主键和 DLL 文件名。改名前先全局搜一遍这个字符串。

5. **`InitController<TModel>` 是泛型基类，所有端点都是 `virtual`**。子类如果 override 了 `Add`
   却忘了调 `business.Add`，或者忘了调 `await base.Add(entity)`，平台级校验和日志就全丢了。

6. **`lib/` 是构建产物却通常进了版本库**。复刻时把它加进 `.gitignore`，或者明确接受它入库并
   在 CI 里保证可重现。

7. **拦截器在类上注册、不在方法上**。`AutofacModuleRegister` 里 `RegisterGeneric(...).EnableInterfaceInterceptors()`
   是按接口注册的，想给单个方法加拦截要额外处理。

8. **`UnAudit` 之类的只读校验要用 `GetCurrentNoTrxDb()`**，不能用 `GetCurrentDb()`——
   后者参与当前事务，在回滚场景下读到的数据不对。

---

## 命名约定

复刻时保持这套命名，否则代码生成器和人的直觉都会错位：

| 前缀 | 含义 | 例 |
|---|---|---|
| `T_` / `t_` | 数据库表实体 | `T_Sample_Master` |
| `f` | 字段 | `fOrderNo`、`fStatusCode` |
| `fC` / `fA` / `fM` | 建立 / 审核 / 修改 | `fCDate` / `fAppDate` / `fModiDate` |
| `Mst` / `lstItem1..N` | DTO 里的主表 / 明细集合 | `entity.Mst`、`entity.lstItem1` |
| `{域}{3位序号}_{名称}` | 模块号 | `MOD001_Sample` |

域前缀：`SYS`(系统) `MST`(基础数据) `SAL`(销售) `PUR`(采购) `INV`(库存) `CST`(成本)
`AR`(应收) `AP`(应付)
