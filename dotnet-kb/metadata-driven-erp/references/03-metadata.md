# 03 · 元数据引擎

> 依据标注同 [01](01-scaffold.md)：`[实现: 路径]` = 取自本仓库；`[来源: id p页码]` = 本地官方 PDF；
> `[联网-官方: URL]` = L1；**「分析」= 我的推断，不是文档结论**。

## 目录

- [1. 三张表](#1-三张表)
- [2. 字段级配置怎么用](#2-字段级配置怎么用)
- [3. 加载与缓存](#3-加载与缓存)
- [4. 第二个来源：编译进 DLL 的 FormConfig](#4-第二个来源编译进-dll-的-formconfig)
- [5. 配置怎么变成 SQL](#5-配置怎么变成-sql)
- [6. 落地建议](#6-落地建议)

---

## 1. 三张表

```
T_META_DataFormMst            一个「功能界面」= 一个 FormCode
   │  PK: fFormCode
   └─ T_META_DataInterMst     一个「接口」= 一张物理表
      │  PK: (fFormCode, fInterCode)
      └─ T_META_DataInterFields  一个「字段」
         PK: (fFormCode, fInterCode, fFieldCode)
```

**层级关系靠 `fInterCode` 和 `fDtoVar` 关联到 DTO：**

| `fDtoVar` 值 | 对应 DTO 属性 | 例 |
|---|---|---|
| `Mst` | 主表对象 | `dto.Mst` |
| `lstItem1` ~ `lstItemN` | 明细集合 | `dto.lstItem1` |

`fUILevel` 决定处理顺序 —— 主表 0，明细依次递增。`AddOperate` 就是按它排序逐层插入的。

### 主表 `T_META_DataFormMst`

`[实现: Proj.Base/Proj.Base.Model/Models/Proj/FormCode/T_META_DataFormMst.cs]`

关键字段：

| 字段 | 含义 |
|---|---|
| `fFormCode` | **主键**。必须等于 DTO 类名去掉 `DTO` |
| `fFormDesc` | 界面名称 |
| `fFormType` / `fSubSys` / `fUrl` | 分类、所属子系统、前端路由 |
| `fIfCallCommonInterface` | 是否走通用接口（默认 true 走通用引擎） |
| `fInterLayers` / `fInterCount` | 层级数 / 接口数 |
| `fIfUseAppPrc` / `fAppPrcCode` | 是否挂审批流 / 流程代号 |
| `fReceiptCode` | 单据编号规则代号（见 `t_Sys_ReceiptMst`） |
| `fUIRateMst` / `fUIRateItem1..5` | 各层界面宽高比 |
| `fExportFile` / `fFrontEndFile` / `fBackEndFile` | 代码生成器产物路径 |

导航属性（不入库）：

```csharp
[SugarColumn(IsIgnore = true)] public List<T_META_DataInterMst> lstInterMst { get; set; }
[SugarColumn(IsIgnore = true)] public string userName { get; set; }
[SugarColumn(IsIgnore = true)] public string userCode { get; set; }
```

`IsIgnore = true` 是 SqlSugar 的「这列不映射到数据库」标记。

### 接口表 `T_META_DataInterMst`

`[实现: .../FormCode/T_META_DataInterMst.cs]`

| 字段 | 含义 |
|---|---|
| `fFormCode` + `fInterCode` | **联合主键** |
| `fEntityCode` | 对应的实体类名（如 `T_Sample_Master`） |
| `fDBTable` | **物理表名**。插入时 `Db.Insertable(item).AS(formInfo.fDBTable)` 用它 |
| `fDtoVar` | **`Mst` / `lstItem1`… 决定这份配置对应 DTO 的哪个属性** |
| `fUILevel` | 处理顺序，主表 0 |
| `fIsMaster` | 是否主表 —— 决定查询时是否附加权限过滤 |
| `fParent` / `fRelateCondition` / `fBlongInterCode` | 父子关系与关联条件 |
| `fSqlFull` / `fSqlSelect` / `fSqlFrom` / `fSqlWhere` / `fSqlGroup` / `fSqlSort` / `fSqlFldList` | 查询 SQL 的零件，运行时拼装 |
| `fSpName` | 走存储过程的接口 |
| `fIfGrpFldDisplayByTab` | 分组字段是否按页签显示 |
| `fGenFrontEndCodeTimes` / `fGenBackEndCodeTimes` | 代码生成器生成次数（幂等标记） |

同样是 `IsIgnore` 的运行时字段：`fReBuildSql_ByBackEnd`、`fData`、`lstInterFields`。

### 字段表 `T_META_DataInterFields`

`[实现: .../FormCode/T_META_DataInterFields.cs]` —— 三张表里**唯一真正影响行为**的一张。
按用途分组：

**主键与标识**

| 字段 | 含义 |
|---|---|
| `fIfPrimaryKey` | 物理主键 —— 插入时被 `IgnoreColumns` 排除（自增） |
| `fIfLogicalPK` / `fLogicalPKGroupCode` | 逻辑主键。同组字段共同构成业务唯一键，用于去重/幂等 |

**增删改行为**

| 字段 | 含义 |
|---|---|
| `fIfUpToSql` | 是否写回数据库 |
| `fIfIndepenUpdate` | 是否走「独立更新」（`IndependentUpdate` 动作） |
| `fIfCanEditByAdd` / `fIfCanEditByEdit` | 新增态 / 修改态可编辑 |
| `fDefault` / `fMaxValue` / `fMinValue` / `fRegular` | 默认值与校验 |

**类型与存储**

`fType`、`fSqlType`、`fMaxLength`、`fScale`、`fDecimalStep`、`fIfNullable`、
`fTblName`、`fTblAlias`、`fSourceColumn`、`fUnionFld`

**界面渲染**（这一段是前端契约，后端不用）

`fInputWay`（输入方式）、`fDataSource`（开窗/下拉配置）、`fFieldLink`（联动）、
`fRenderer`、`fColWidth`、`fColOrder`、`fGroupCode` / `fGroupSNo`、
`fIfVisibleOnEditPage`、`fIfForceShow`、`fIfForceHide`、`fIfMobileShow`、
`fIfShowFilter`、`fIfKey`、`fIfRoleApply`、`fIfAppShow`

**多语言**：`fFieldDesc` + `fLangID`

## 2. 字段级配置怎么用

以 `fDataSource` 为例 —— 一个字段的开窗配置，是一串方括号分段的自定义格式：

```
[FormCode=REF001_Query,InterCode=ItemBaseQuery]|[Filter=fItemTypeCode IN ('20','30')]|[MultiSelect=0]|[MultiSelRtnOneRow=0]|[fItemCode=fItemCode,...]|[ExtendFilter=]|[HideCols=fItemID]|[RepeatSelSameRow=0]
```

| 段 | 含义 |
|---|---|
| `FormCode` + `InterCode` | 开窗打开的**另一个 FormCode** —— 复用同一套元数据 |
| `Filter` | 强制附加的 where 条件 |
| `MultiSelect` / `MultiSelRtnOneRow` | 多选 / 多选是否只回填一行 |
| `fItemCode=fItemCode,...` | **字段映射**：左边是开窗里的列，右边是本表单的列 |
| `ExtendFilter` | 运行时动态追加的条件 |
| `HideCols` | 开窗里隐藏的列（主键等） |
| `RepeatSelSameRow` | 允许同一行重复选 |

**这个格式是这套系统里最有复用价值的设计**：不需要为「销售订单选商品」写一个专用接口，
只要配一个 `fDataSource` 指向已有的商品查询 FormCode 就行。
解析在 `FormCodeHelper.GetDataSourceSql` 里。

## 3. 加载与缓存

`[实现: Proj.Base/Proj.Base.Common/Develop/DevelopHelper.cs:13-62]`：

```csharp
public static class DevelopHelper
{
    private static readonly ISqlSugarClient Db;
    private static readonly ConnectionMultiplexer _redis;

    static DevelopHelper()
    {
        Db = App.GetCurrentDb();                       // ← 静态构造，只跑一次
        _redis = App.GetService<ConnectionMultiplexer>();
    }

    public static T_META_DataFormMst GetDataFormMst(string FromCode)
    {
        T_META_DataFormMst from = null;
        try
        {
            IDatabase redisDb = _redis.GetDatabase();
            var formCodeKey = $"{App.User.fMandt}_{App.User.fAccountID}_FormCode_{FromCode}";
            var cachedformCode = redisDb.StringGet(formCodeKey);
            if (!cachedformCode.IsNullOrEmpty)
                from = JsonConvert.DeserializeObject<T_META_DataFormMst>(cachedformCode);

            if (from?.fFormCode == null)
            {
                var interMst = Db.Queryable<T_META_DataInterMst>()
                    .Where(fields => fields.fFormCode == FromCode).ToList();
                var interFields = Db.Queryable<T_META_DataInterFields>()
                    .Where(fields => fields.fFormCode == FromCode).ToList();

                from = Db.Queryable<T_META_DataFormMst>().With(SqlWith.NoLock)
                       .First(o => o.fFormCode == FromCode);

                if (from != null)
                {
                    from.lstInterMst = interMst;
                    foreach (var item in from.lstInterMst)
                        item.lstInterFields = interFields
                            .Where(o => o.fInterCode == item.fInterCode).ToList();
                }
                var serializedDataForm = JsonConvert.SerializeObject(from);
                redisDb.StringSet(formCodeKey, serializedDataForm, TimeSpan.FromHours(48));
            }
        }
        // ...catch
        return from;
    }
}
```

### 这里有一个真实的缺陷

**缓存键是按账套分的**（`{fMandt}_{fAccountID}_FormCode_{FromCode}`），
**但读库用的 `Db` 是静态字段，在静态构造里捕获 `App.GetCurrentDb()` 之后永不更新。**

分析：`DevelopHelper` 第一次被任何请求碰到时，`App.User` 是**那个请求**的用户，
`GetCurrentDb()` 于是返回**那个账套**的连接。此后所有账套的元数据都从这一个库里读，
只是往各自账套的 Redis key 下写。

后果：如果 A 账套和 B 账套的表单元数据不一致（这在多客户场景下是常态），
先访问的那个账套的配置会被**缓存到所有账套**下 —— 而且缓存 48 小时。
症状是「B 客户改了字段配置不生效」，排查时看 Redis 有值、看 DB 也有值，非常难定位。

**复刻时改成每次调用实时取库**：

```csharp
public static T_META_DataFormMst GetDataFormMst(string FromCode)
{
    // 不要缓存 ISqlSugarClient —— 账套是随请求变的
    var db = App.GetCurrentNoTrxDb();
    // ... 后续用 db 而不是 Db
}
```

Redis 只缓存**结果**，不缓存**连接**。这是多租户系统里一条通用的纪律：
**任何进静态字段的东西都会跨租户泄漏。**

### 其他细节

- `.With(SqlWith.NoLock)` —— 加 `NOLOCK` 提示，配置表读多写少，可以接受脏读
- 缓存 48 小时。改配置后要么等过期，要么主动删 key —— **建议在配置维护界面提供一个
  「刷新缓存」按钮**，否则现场改配置的人会以为系统坏了
- `JsonConvert` 是 Newtonsoft，不是 `System.Text.Json`。整个仓库统一用 Newtonsoft
  （`Proj.Base.Json` 是它的一层包装）

## 4. 第二个来源：编译进 DLL 的 FormConfig

`[实现: Proj.Base/Proj.Base.Common/Helper/Form/FormCodeHelper.cs]`：

```csharp
public T_META_DataFormMst GetInitConfig(string FormCode)
{
    var basePath = AppContext.BaseDirectory;
    var dllPath = Path.Combine(basePath, FormCode + ".Services.dll");
    Assembly assembly = Assembly.LoadFrom(dllPath);
    Type type = assembly.GetType(FormCode + ".Services.FormConfig");
    object instance = Activator.CreateInstance(type);
    MethodInfo method = type.GetMethod("InitFormConfig",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    T_META_DataFormMst result = (T_META_DataFormMst)method.Invoke(instance, null);
    return result;
}
```

它反射调用模块里一个**代码生成的** `FormConfig` 类，返回一份**硬编码在 C# 里的元数据**。
`[实现: Business/MST/MOD002_Demo/MOD002_Demo.Services/MOD002_DemoServices.Config.cs]`：

```csharp
namespace MOD002_Demo.Services
{
    public class FormConfig
    {
        public T_META_DataFormMst InitFormConfig()
        {
            T_META_DataFormMst dataFormMst = new T_META_DataFormMst();
            Write_DataFormMst(ref dataFormMst);
            Write_DataInterMst(ref dataFormMst);
            return dataFormMst;
        }

        private void Write_DataFormMst(ref T_META_DataFormMst dataFormMst)
        {
            dataFormMst = new T_META_DataFormMst
            {
                fFormCode = "MOD002_Demo",
                fFormDesc = "示例单据",
                // ... 逐个字段赋值
            };
        }
    }
}
```

**它的唯一调用点是兜底**：`FormCodeHelper.GetDataSourceSql` 里
`if (dataFormMst is null) { dataFormMst = GetInitConfig(FormCode); }` ——
只有在开窗/下拉取数、且 DB 里查不到该 FormCode 的元数据时才用。

⚠️ 这里有两个问题：

1. **路径不一致。** `GetInitConfig` 用 `AppContext.BaseDirectory`，而 `BusinessProvider`
   用 `Directory.GetCurrentDirectory()`。两者在 `dotnet run` 下通常相同，
   但在 Windows 服务 / 不同工作目录下会分叉。
2. **两份真身没有一致性校验。** DB 改了、`FormConfig` 没重新生成，两者就永久不一致，
   而且没有任何提示。排查「为什么列表页显示正常但开窗少一个字段」会非常痛苦。

## 5. 配置怎么变成 SQL

`FormCodeHelper` 是元数据到 SQL 的翻译层，主要入口：

| 方法 | 作用 |
|---|---|
| `ListDicToSql` | 把前端传来的字典筛选条件转成 where |
| `GetWhereString` / `GetChildWhereDicByRelation` | 拼 where，含父子关联条件 |
| `GetOrderString` | 拼 order by |
| `GetDataSourceSql` | 解析 `fDataSource` 配置，生成开窗查询；**这里是 `GetInitConfig` 的兜底点** |
| `ChangeForCheckBox` / `ChangeForString` | 值转换（bool ↔ 字符串等） |

下游是 `FormCommonRepository.CommonQuery`，优先级：

```
fReBuildSql_ByBackEnd（后端重建的完整 SQL，最高）
  > fSpName（存储过程）
    > fSqlSelect + fSqlFrom + fSqlWhere + fSqlGroup + fSqlSort 拼装
```

`fIsMaster` 的接口会在 SQL 上再叠一层权限过滤（`Rights`）——
所以**主表配置错 `fIsMaster`，权限就不生效**，而且不会报错，只会越权看到别人的数据。

## 6. 落地建议

如果是从零复刻，元数据这块值得做三个改进：

1. **`DevelopHelper` 别缓存 `ISqlSugarClient`**（见上）
2. **配置维护界面加「刷新缓存」**，删 Redis key 即可
3. **`GetInitConfig` 要么删掉，要么加一致性校验** —— 在启动自检里比对 DB 元数据和
   编译期 `FormConfig` 的字段集合，不一致就告警。两份真身静默分叉是这类系统最常见的事故源

继续读 [04-operate-engine.md](04-operate-engine.md)。
