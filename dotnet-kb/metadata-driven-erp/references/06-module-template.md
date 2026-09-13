# 06 · 业务模块模板

> 依据标注同 [01](01-scaffold.md)。本文件里的模板代码**逐字取自**
> `Business/SAL/MOD001_Sample/`，只把业务专属的名字换成占位符。
> 这是可以直接复制编译的形态。

## 目录

- [1. 四件套](#1-四件套)
- [2. Model 层](#2-model-层)
- [3. Business 层](#3-business-层)
- [4. AOP 动作类](#4-aop-动作类)
- [5. Services 层](#5-services-层)
- [6. 元数据配置](#6-元数据配置)
- [7. 权限配置](#7-权限配置)
- [8. 交付前检查清单](#8-交付前检查清单)

---

## 1. 四件套

命名约定：`{域}{3位序号}_{名称}`，例 `MOD001_Sample`。
下面的模板用 `MOD001_Sample` 做具体例子，把占位符替换掉即可。

```
Business/SAL/MOD001_Sample/
├── MOD001_Sample.Model/
│   ├── MOD001_SampleDTO.cs        主表 Mst + 明细 lstItem1
│   ├── T_Sample_Master.cs         主表实体
│   └── T_Sample_Item.cs           明细实体
├── MOD001_Sample.Business/
│   ├── MOD001_SampleBusiness.cs   IBusiness<DTO> 实现
│   └── Actions/
│       ├── CheckAddAop.cs                新增前校验
│       └── UpdateAop.cs                  修改前校验
└── MOD001_Sample.Services/
    ├── Facade/MOD001_SampleController.cs   空壳控制器
    └── MOD001_Sample.Services.csproj
```

**三个必须一致的字符串**（对不上就静默失效）：

| 位置 | 值 |
|---|---|
| DTO 类名 | `MOD001_SampleDTO` |
| Business DLL 文件名 | `MOD001_Sample.Business.dll` |
| 元数据 `fFormCode` / `fInterCode` | `MOD001_Sample` |

推导规则：`typeof(DTO).Name.Replace("DTO","")` → DLL 名；
`Regex.Replace(typeof(DTO).Name, "DTO$", "")` → FormCode。

## 2. Model 层

### DTO

`[实现: Business/SAL/MOD001_Sample/MOD001_Sample.Model/MOD001_SampleDTO.cs]`

```csharp
namespace MOD001_Sample.Model
{
    public class MOD001_SampleDTO
    {
        public T_Sample_Master Mst { get; set; }
        public List<T_Sample_Item>? lstItem1 { get; set; }
    }
}
```

**属性名必须是 `Mst`、`lstItem1`、`lstItem2`…** —— 元数据的 `fDtoVar` 直接拿这些名字去
`GetProperty` 反射。名字不对，`property` 为 `null`，`Add` 里那段 `if (property != null)`
会**静默跳过**，明细一条都不插，还不报错。

### 主表实体

`[实现: .../T_Sample_Master.cs]` 的形态：

```csharp
[SugarTable("T_Sample_Master", "Proj")]
public class T_Sample_Master : OperateEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int? fOrderID { get; set; }

    public string fOrderNo { get; set; }        // 单据号
    public DateTime? fDate { get; set; }
    public string fStatusCode { get; set; }       // 状态
    public string fStatus { get; set; }
    public int? fDeptID { get; set; }
    public int? fEmpID { get; set; }
    public string fSourceNo { get; set; }         // 来源单号
    public string fSrcType { get; set; }        // 来源类型
    public string fRemark { get; set; }

    // 单据标准字段
    public string fCreator { get; set; }
    public DateTime? fCDate { get; set; }
    public string fApprover { get; set; }
    public DateTime? fAppDate { get; set; }
    public string fModifier { get; set; }
    public DateTime? fModiDate { get; set; }
    public string fCommit { get; set; }

    // 仅用于显示，不入库
    [SugarColumn(IsIgnore = true)] public string fDeptCode { get; set; }
    [SugarColumn(IsIgnore = true)] public string fDeptName { get; set; }
    [SugarColumn(IsIgnore = true)] public string fUserCode { get; set; }
    [SugarColumn(IsIgnore = true)] public string fUserName { get; set; }
}
```

四条规则：

1. **继承 `OperateEntity`** —— 它带 `operate`（`OperateEnum`）和 `originDic` 两个 `IsIgnore` 字段，
   引擎靠它们在动作间传递操作类型和原始数据快照
2. **`[SugarTable(表名, "Proj")]`** —— 第二个参数是 schema。**实体类名与表名保持一致**
   （原因见 [04 §4 第②点](04-operate-engine.md#几个必须理解的细节)：`Add` 里主表用的是
   `Mst.GetType().Name` 而不是配置的表名）
3. **自增主键用 `int?` 可空** —— 新增时是 `null`，插入后 SqlSugar 回填
4. **join 出来的展示字段一律 `[SugarColumn(IsIgnore = true)]`** —— 漏标会在查询时
   试图 select 一个不存在的列

## 3. Business 层

这是「只写例外」的完整形态 `[实现: .../MOD001_SampleBusiness.cs]`。
**没写代码的方法就是一行委托，这是正常的，不是没写完。**

```csharp
using CustomAttribute;
using MOD001_Sample.Business.Actions;
using MOD001_Sample.Model;
using SqlSugar;
using Proj.Base.Common;
using Proj.Base.IServices;
using Proj.Base.Model;
using Proj.Base.Model.Systems.Excel;
using Proj.Base.Services;

namespace MOD001_Sample.Business
{
    [CusTrans]
    public class MOD001_SampleBusiness : IBusiness<MOD001_SampleDTO>
    {
        protected IBusinessBase<MOD001_SampleDTO> _businessBase;
        private readonly ISqlSugarClient notrxDb = App.GetCurrentNoTrxDb();

        public MOD001_SampleBusiness()
        {
            // 注意：不是构造函数注入，是服务定位器
            _businessBase = App.GetService<IBusinessBase<MOD001_SampleDTO>>();
        }

        // ============ 需要写代码的动作 ============

        [CusAction(nameof(CheckAddAop))]
        public async Task<ApiResult> Add(MOD001_SampleDTO entity)
        {
            ApiResult result = new ApiResult() { success = true };
            if (result.success) result = await _businessBase.Add(entity);
            return result;
        }

        [CusAction(nameof(UpdateAop))]
        public async Task<ApiResult> Update(MOD001_SampleDTO entity)
        {
            ApiResult result = new ApiResult() { success = true };
            if (result.success) result = await _businessBase.Update(entity);
            return result;
        }

        public async Task<ApiResult> UnAudit(MOD001_SampleDTO entity)
        {
            ApiResult result = new ApiResult() { success = true };
            if (await notrxDb.Queryable<T_Sample_Master>()
                    .Where(x => x.fOrderID == entity.Mst.fOrderID).AnyAsync())
            {
                result.success = false;
                result.msg = "有来源单号的不允许反审核";
                return result;
            }
            if (result.success) result = await _businessBase.UnAudit(entity);
            return result;
        }

        // ============ 纯委托的动作 ============

        public async Task<ApiResult> Delete(MOD001_SampleDTO entity)
        {
            ApiResult result = new ApiResult() { success = true };
            if (result.success) result = await _businessBase.Delete(entity);
            return result;
        }

        public async Task<ApiResult> Audit(MOD001_SampleDTO entity) { /* 同上形态 */ }
        public async Task<ApiResult> Submit(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> UnSubmit(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> Enable(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> UnEnable(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> Import(ImportData entity) { /* ... */ }
        public async Task<ApiResult> Export(SysSingleTableDTO entity) { /* ... */ }
        public async Task<ApiResult> Post(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> UnPost(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> Close(MOD001_SampleDTO entity) { /* ... */ }
        public async Task<ApiResult> UnClose(MOD001_SampleDTO entity) { /* ... */ }

        [CusNoTrans]
        public async Task<ApiResult> CommonQuery(SysSingleTableDTO dto)
        {
            ApiResult result = new ApiResult() { success = true };
            if (result.success) result = await _businessBase.CommonQuery(dto);
            return result;
        }
    }
}
```

### 必须遵守的写法

**① 类上标 `[CusTrans]`。** 虽然当前实现里这个特性不生效（见 [SKILL.md](../SKILL.md) 坑 1），
但代码生成器会生成它，保持一致。

**② 类型 `IBusiness<DTO>` 必须显式写出。** `BusinessProvider` 靠
`typeof(IBusiness<T>).IsAssignableFrom(t)` 找实现类。

**③ `_businessBase` 用服务定位器拿，不是构造函数注入。**
因为 `BusinessProvider` 是 `Activator.CreateInstance` 造的业务类实例 —— 构造函数参数没人给它填。
**所以业务类只能有无参构造函数**，或者像这里一样完全不依赖注入。

**④ `result.success` 的链式判断是个模式，不是冗余。**
```csharp
ApiResult result = new ApiResult() { success = true };
if (result.success) result = await _businessBase.Xxx(entity);
```
它给了你一个插入点：想在调用前拒绝，写 `result.success = false; result.msg = "...";`
后面那句就不会执行。**但要注意 `_businessBase` 调用之后再设 `result.success = false`
是没用的**（那时已经提交了）。

**⑤ `[CusAction(nameof(XxxAop))]` 里的名字 = AOP 的类名。**
代理靠 `actionnames.Contains(x.GetType().Name)` 匹配。**写字符串字面量而不是 `nameof`
是错的** —— 改名时不会报错。

**⑥ `CommonQuery` 标 `[CusNoTrans]`。** 虽然当前实现里它不生效（事务判定硬编码了
`methodKey == "CommonQuery"`），但保持标注能让未来真的接上特性时行为正确。

### 什么时候该在这里写代码

只写**这个模块特有**的逻辑。判断标准：

| 想做 | 写在这？ | 还是配元数据？ |
|---|---|---|
| 字段必输 | ✗ | ✓ `fIfNullable` |
| 字段最大值 | ✗ | ✓ `fMaxValue` |
| 单据号自动生成 | ✗ | ✓ `fReceiptCode` |
| 建立时间/人自动填 | ✗ | ✓ `BaseOperateHelper.UpdateStatus` |
| 明细回填主表主键 | ✗ | ✓ `BaseOperateHelper.CommonLogic` |
| 保存后通知某人 | ✓ | — |
| 数量不能超过库存 | ✓ | — |
| 审核后回写上游单据 | ✓ | — |

## 4. AOP 动作类

`[实现: .../Actions/CheckAddAop.cs]`。**放在 `Actions/` 子目录，和业务类同一个项目** ——
`BusinessProvider` 是从 Business DLL 里扫 `IBusinessAop` 实现的，放到别的项目就找不到。

```csharp
public class CheckAddAop : BusinessBaseAop
{
    public async override Task Before(BusinessAopContext aopContext)
    {
        ApiResult apiResult = new ApiResult();
        apiResult.success = true;

        // AOP 是 Activator.CreateInstance 造的，没有构造函数注入 —— 要 DI 就自己开 scope
        using var scope = App.RootServices.CreateAsyncScope();
        var locator = scope.ServiceProvider.GetRequiredService<IExtendedResourceManager>();

        // 只读校验用 NoTrx
        using var sqlClient = App.GetCurrentNoTrxDb();

        var dto = aopContext.Parameters[0] as MOD001_SampleDTO;

        // 1. 单据号：配了编号规则就用规则生成，没配就必须手工填
        var hasRule = sqlClient.Queryable<t_Sys_ReceiptMst>()
            .Where(x => x.fTableName == dto.Mst.GetType().Name).Any();
        if (!hasRule && string.IsNullOrEmpty(dto.Mst.fOrderNo))
        {
            apiResult.success = false;
            apiResult.msg = "单据编码不能为空";
            aopContext.BeforeResult = apiResult;
            return;                                   // ← 短路：业务方法不会执行
        }

        // 2. 单据号重复
        if (sqlClient.Queryable<T_Sample_Master>()
                .Where(x => x.fOrderNo == dto.Mst.fOrderNo).Any())
        {
            apiResult.success = false;
            apiResult.msg = "单据编码重复";
            aopContext.BeforeResult = apiResult;
            return;
        }

        // 3. 默认值：这里改的是 args 里的对象引用，会传进业务方法
        if (!dto.Mst.fDate.HasValue || !DateTime.TryParse(dto.Mst.fCDate.ToString(), out DateTime _))
            dto.Mst.fDate = DateTime.Now;

        dto.Mst.fSrcType = "10";                    // 此接口来源类型为手工录入

        // 4. 明细校验
        var seqNo = sqlClient.Queryable<T_Sample_Item>()
            .OrderBy(x => x.fSeqNo).Select(x => x.fSeqNo).First() ?? 0;

        var itemsIds = dto.lstItem1.Select(x => x.fRefOrderID).ToList();
        var refItems = sqlClient.Queryable<T_Ref_Sample>()
            .Where(x => itemsIds.Contains(x.fRefOrderID)).ToList();

        foreach (var item in dto.lstItem1)            // ← 用 foreach，不要用 ForEach(lambda)
        {
            ++seqNo;
            item.fSeqNo = seqNo;

            var refItem = refItems.FirstOrDefault(x => x.fRefOrderID == item.fRefOrderID);
            if (refItem == null)
            {
                apiResult.success = false;
                apiResult.msg = $"行 {item.fSeqNo}：找不到关联的预测单";
                break;                                // ← break 才真的中断
            }
            if (item.fUnitQty == 0)
            {
                apiResult.success = false;
                apiResult.msg = "单位数量不能为 0";
                break;
            }
            if (item.fQty > refItem.fQty - refItem.fDoneQty)
            {
                apiResult.success = false;
                apiResult.msg = "数量超出库存";
                break;
            }
            item.fBaseQty = item.fQty * item.fBaseUnitQty / item.fUnitQty;
            item.fSeqNo = refItem.fSeqNo;
        }

        aopContext.BeforeResult = apiResult;          // ← 必须赋值
    }
}
```

### 现版 `CheckAddAop.cs` 的六个问题 —— 别照抄

`[实现: Business/SAL/MOD001_Sample/MOD001_Sample.Business/Actions/CheckAddAop.cs]`
的当前版本**功能是对的**（能跑通），但有几处写法是错的。上面的模板已经修掉了，
列出来是为了让你知道**为什么模板长那样**：

| 现版写法（行号） | 问题 |
|---|---|
| `lstItem1.ForEach(item => { … if (失败) { …; return; } … })`（53,62） | **`return` 只退出 lambda，不退出 `ForEach`**。后续行照常处理，`apiResult.success = false` 可能被后一行的成功路径盖掉。用 `foreach` + `break` |
| `query_T_Ref_Samples.Where(x => x.fRefOrderID == item.fRefOrderID)?.First()`（57） | 两个错：`Where` **永远不返回 `null`**，`?.` 是无效的防卫；`First()` 在空集合上**抛异常**，会被 `Invoke` 的 catch 变成一条没有行号的 `msg`。用 `FirstOrDefault()` + 判空 |
| `T_Ref_Sample.fQty - T_Ref_Sample.fDoneQty`（58） | 上一行可能返回 `null`，这里直接解引用。**和上一条是同一个根因** |
| `item.fQty * item.fBaseUnitQty / item.fUnitQty`（64） | **除零**。`fUnitQty` 为 0 时抛 `DivideByZeroException`。先校验 |
| `Queryable<...>().OrderBy(x => x.fSeqNo).Select(x => x.fSeqNo).First() ?? 0`（49） | 表为空时 `First()` **先抛异常**，`?? 0` 永远轮不到。用 `FirstOrDefault()` |
| `using var sqlClient = App.GetCurrentDb();`（26） | `Before` 里全是**只读校验**，应该用 `App.GetCurrentNoTrxDb()`。用事务连接会让这些读挂在当前事务上、持锁，长事务下容易死锁（理由见 [05 §2](05-data-and-tenant.md#2-切库入口-getcurrentdb)） |

另外（45）`!salesOrder.Mst.fDate.HasValue || !DateTime.TryParse(fCDate.ToString(), out _)`
这个条件有个副作用：**`fDate` 有值但 `fCDate` 为空时，会把用户填的 `fDate` 覆盖成 `DateTime.Now`。**
如果 `fCDate`（建立时间）本来就该由 `BaseOperateHelper.UpdateStatus` 填，
这里就不该读它 —— 判断「日期是否为空」只看 `fDate` 就够了。

`UnAudit` 里也有一处**逻辑可疑**（原版）：

```csharp
if (await notrxDb.Queryable<T_Sample_Master>()
        .Where(x => x.fOrderID == entity.Mst.fOrderID).AnyAsync())
{
    result.success = false;
    result.msg = "有来源单号的不允许反审核";
    return result;
}
```

提示语说的是「有来源单号」，但条件查的是「主表存在这条记录」—— **这会导致所有反审核都被拒绝**。
看着应该是 `.Where(x => x.fOrderID == ... && !string.IsNullOrEmpty(x.fSourceNo))`。
**改这类地方前先跟业务确认**，不要凭提示语猜。

## 5. Services 层

### 控制器：空壳

`[实现: .../Facade/MOD001_SampleController.cs]`

```csharp
using MOD001_Sample.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proj;
using Proj.Base.IServices;
using Proj.Base.Model;
using Proj.Base.Reflect;
using Proj.Base.Action;
using Microsoft.Extensions.Logging;
using Proj.Base.Model.Systems.Excel;

namespace MOD001_Sample.Services
{
    [ApiExplorerSettings(GroupName = "Proj")]
    [Route("api/ERP/[controller]/[action]")]
    [Authorize(Policy = Permissions.Name)]
    public class MOD001_SampleController : InitController<MOD001_SampleDTO>
    {
        public MOD001_SampleController(IBusinessProvider provider,
                                               ILogger<MOD001_SampleController> logger)
            : base(logger)
        {
        }
    }
}
```

**就这些，没有别的。** 所有端点（Add/Update/Delete/Audit/…/CommonQuery 共 20 个左右）
都从 `InitController<TModel>` 继承。

⚠️ `IBusinessProvider provider` 这个构造参数**没被使用** —— `InitController` 自己
从 `App.GetService<IBusinessProvider>()` 拿（见 [SKILL.md](../SKILL.md) 坑 5 相关）。
保留它是代码生成器的习惯，删掉也能编译。

**路由是 `api/ERP/{Controller}/{Action}`**，`{Controller}` 取类名去掉 `Controller` ——
所以 URL 是 `api/ERP/MOD001_Sample/Add`。

### csproj

`[实现: .../MOD001_Sample.Services.csproj]` —— **逐字照抄，只改模块名**：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.Action\Proj.Base.Action.csproj" />
    <ProjectReference Include="..\MOD001_Sample.Business\MOD001_Sample.Business.csproj" />
    <ProjectReference Include="..\MOD001_Sample.Model\MOD001_Sample.Model.csproj" />
    <ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.Services\Proj.Base.Services.csproj" />
    <ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.IServices\Proj.Base.IServices.csproj" />
    <ProjectReference Include="..\..\..\..\Proj.Base\Proj.Base.Reflect\Proj.Base.Reflect.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AppendTargetFrameworkToOutputPath>output</AppendTargetFrameworkToOutputPath>
    <OutputPath>..\..\..\..\Proj.Api\lib</OutputPath>
  </PropertyGroup>

</Project>
```

`AppendTargetFrameworkToOutputPath` 被设成 `output`（而不是 `true`）——
效果是不追加 `net8.0` 子目录，DLL 直接落到 `lib/`。本仓库 `lib/` 下有 262 个文件，
组织方式证实了这一点。

**`.Model` 和 `.Business` 两个 csproj 不需要设 `OutputPath`** —— 它们会被
`.Services` 一起带到 `lib/`（`ProjectReference` 的产物会复制到引用方的输出目录）。

## 6. 元数据配置

代码写完了，**没有元数据这个模块跑不起来** —— `DevelopHelper.GetDataFormMst` 返回 `null`，
`Add` 里 `dataFormMst.lstInterMst` 直接 NRE。

需要往三张表插数据。最小的一个主从单据：

```sql
-- ① 表单主配置
INSERT INTO T_META_DataFormMst (fFormCode, fFormDesc, fFormType, fSubSys, fIfCallCommonInterface,
                                fInterLayers, fInterCount, fReceiptCode)
VALUES ('MOD001_Sample', N'示例单据', 'MstItem', 'SAL', 1, 2, 2, 'SAMPLE');

-- ② 接口配置：主表一条、明细一条
INSERT INTO T_META_DataInterMst (fFormCode, fInterCode, fInterDesc, fEntityCode, fDBTable,
                                 fDtoVar, fUILevel, fIsMaster, fParent)
VALUES ('MOD001_Sample', 'Mst', N'主表', 'T_Sample_Master', 'T_Sample_Master',
        'Mst', 0, 1, NULL),
       ('MOD001_Sample', 'Item1', N'明细', 'T_Sample_Item', 'T_Sample_Item',
        'lstItem1', 1, 0, 'Mst');

-- ③ 字段配置：每个要读写的列都要有一条
INSERT INTO T_META_DataInterFields
    (fFormCode, fInterCode, fFieldCode, fFieldDesc, fType, fSqlType, fMaxLength,
     fIfNullable, fIfPrimaryKey, fIfLogicalPK, fIfUpToSql)
VALUES
    -- 主表主键：自增，fType 必须是 'Identity'
    ('MOD001_Sample', 'Mst', 'fOrderID', N'冲销单ID', 'Identity', 'int', 4,
     0, 1, 0, 0),
    ('MOD001_Sample', 'Mst', 'fOrderNo', N'冲销单号', 'String', 'nvarchar', 50,
     1, 0, 1, 1),
    ('MOD001_Sample', 'Mst', 'fDate',      N'单据日期', 'Date',   'datetime', 8,
     1, 0, 0, 1),
    ('MOD001_Sample', 'Mst', 'fRemark',    N'备注',     'String', 'nvarchar', 200,
     1, 0, 0, 1),
    -- 明细：主键 Identity，外键指回主表
    ('MOD001_Sample', 'Item1', 'fOrderItemID', N'明细ID',   'Identity', 'int',      4,
     0, 1, 0, 0),
    ('MOD001_Sample', 'Item1', 'fOrderID',     N'冲销单ID', 'Number',   'int',      4,
     1, 0, 0, 1),
    ('MOD001_Sample', 'Item1', 'fQty',           N'数量',     'Number',   'decimal', 18,
     1, 0, 0, 1);
```

### 配置要点

| 要点 | 说明 |
|---|---|
| `fType = 'Identity'` | **自增列必须这么标**，否则 `IgnoreColumns(primaryKeyIdentity)` 不排除它，插入报错 |
| `fIfPrimaryKey = 1` 且 `fIfUpToSql = 0` | 自增列的标准组合 |
| `fIfLogicalPK = 1` | 业务唯一键（如单据号），用于查重 |
| `fIfUpToSql = 0` | 该列不参与 insert/update —— 只读字段 |
| `fUILevel` | **主表必须是 0，明细递增**。反了的话明细先插，外键填不上 |
| `fDtoVar` | 必须**精确等于** DTO 里的属性名（`Mst` / `lstItem1`） |
| `fParent` | 明细指回主表的 `fInterCode`，`CommonLogic` 靠它回填外键 |
| `fDBTable` | 物理表名。**目前主表插入实际用的是实体类名**，两者保持一致最安全 |

改完配置**记得清 Redis**，元数据缓存 48 小时：

```
DEL {fMandt}_{fAccountID}_FormCode_MOD001_Sample
```

### 可选的 FormConfig

模块里可以再放一个 `MOD001_Sample.Services.FormConfig` 类，
让 `FormCodeHelper.GetInitConfig` 能在 DB 元数据缺失时兜底（见
[03 §4](03-metadata.md#4-第二个来源编译进-dll-的-formconfig)）。
**它不是必须的** —— SAL009 就没有这个文件，而 MST025 有。
只有在需要开窗/下拉且该 FormCode 不在 DB 里时才需要。

## 7. 权限配置

**加完模块不配权限，接口会 403。** 三张表：

| 表 | 作用 |
|---|---|
| `T_Sys_Function_Library` | 功能清单：URL + 功能名 |
| `T_Sys_FunRights` | 角色 → 功能 的映射 |
| `T_Sys_Process` | 审批流定义（用了 `fIfUseAppPrc` 才有） |

每个要暴露的动作都要在 `T_Sys_Function_Library` 里有一条 URL 记录，
形如 `api/ERP/MOD001_Sample/Add`。`PermissionHandler` 拿
`CanRunURL(questUrl, fUserCode)` 去查。

**超管（`fUserType == "99"`）跳过检查** —— 所以「开发环境能点、测试账号点不了」
通常就是权限没配，不是代码问题。

## 8. 交付前检查清单

加完一个模块，逐条过一遍。**前五条会静默失效**，不报错但功能不对：

- [ ] `MOD001_Sample.Services.csproj` 的 `OutputPath` 指向 `Proj.Api/lib`
- [ ] 重新编译，确认 `lib/MOD001_Sample.Services.dll` 和 `.Business.dll` 都在
- [ ] `appsettings.json` 的 `ServiceList` 里有 `SAL`（域前缀）
- [ ] DTO 类名 = 元数据 `fFormCode` = DLL 文件名前缀（三处一字不差）
- [ ] 元数据三张表都配了，`fUILevel` 主表 0、明细 1，`fDtoVar` 与 DTO 属性名一致
- [ ] 自增主键的 `fType = 'Identity'`，`fIfPrimaryKey = 1`，`fIfUpToSql = 0`
- [ ] 清掉 Redis 里该 FormCode 的缓存
- [ ] `T_Sys_Function_Library` 里配了各动作的 URL，并授予了测试角色
- [ ] 跑通一遍：新增 → 查询列表 → 编辑 → 审核（→ 弃审）
- [ ] 业务类和 AOP 类在**同一个项目**里

最后一步跑通之前，不要开始下一个模块。这套架构的失败模式是「编译通过但运行不对」，
攒着一起测只会让定位成本指数上升。

---

回到 [SKILL.md](../SKILL.md) 或按需查阅其他 reference。
