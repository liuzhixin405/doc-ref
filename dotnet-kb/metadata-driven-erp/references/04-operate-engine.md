# 04 · 通用动作引擎

> 依据标注同 [01](01-scaffold.md)：`[实现: 路径]` = 取自本仓库；`[来源: id p页码]` = 本地官方 PDF；
> `[联网-官方: URL]` = L1；**「分析」= 我的推断，不是文档结论**。

## 目录

- [1. 分层的意义](#1-分层的意义)
- [2. 动作集](#2-动作集)
- [3. 门面 BaseOperateServices](#3-门面-baseoperateservices)
- [4. Add 策略：元数据驱动的主循环](#4-add-策略元数据驱动的主循环)
- [5. 异常与事务的配合](#5-异常与事务的配合)
- [6. 缓存策略实例](#6-缓存策略实例)
- [7. DI 注册](#7-di-注册)
- [8. 已知问题](#8-已知问题)

---

## 1. 分层的意义

```
InitController<TModel>          泛型控制器（HTTP 层）
        ↓
IBusiness<T>                    业务层（模块自己实现，可挂 AOP）
        ↓
BusinessBase<T>                 纯转发，一行一个动作
        ↓
IBaseOperateServices<T>         门面：把元数据配给每个策略
        ↓
IAddOperate<T> / IUpdateOperate<T> / ...    策略实现（通用逻辑在这）
        ↓
IBaseRepository<T> / FormCommonRepository   仓储
```

三层各管一件事，**边界不能混**：

| 层 | 管什么 | 不该管什么 |
|---|---|---|
| `Business<T>` | 这个模块**特有**的规则 | 通用校验、事务、日志 |
| `BaseOperateServices<T>` | 把元数据**配**给正确的策略 | 具体 SQL |
| 策略实现 | **通用**的增删改查逻辑 | 业务语义 |

`BusinessBase<T>` 是纯转发 `[实现: Proj.Base/Proj.Base.Services/Business/BusinessBase.cs]`：

```csharp
public class BusinessBase<TEntity> : IBusinessBase<TEntity> where TEntity : class
{
    public IBaseOperateServices<TEntity> OperateServices { get; set; }

    public BusinessBase(IBaseOperateServices<TEntity> baseOperate) { OperateServices = baseOperate; }

    public virtual Task<ApiResult> Add(TEntity entity) => OperateServices.Add(entity);
    // ... 每个动作都是一行
}
```

看着像多余的样板。**它的价值在于给业务层一个可注入、可 mock、可替换的接缝** ——
业务类只依赖 `IBusinessBase<T>`，不依赖策略、不依赖仓储。

## 2. 动作集

`IBusiness<T>` 的完整契约 `[实现: Proj.Base/Proj.Base.Reflect/IBusiness.cs]`：

```csharp
public interface IBusiness<T> where T : class
{
    Task<ApiResult> Add(T entity);
    Task<ApiResult> Update(T entity);
    Task<ApiResult> IndependentUpdate(T entity);   // 只更新部分字段
    Task<ApiResult> Delete(T entity);
    Task<ApiResult> Audit(T entity);               // 审核
    Task<ApiResult> UnAudit(T entity);             // 弃审
    Task<ApiResult> Submit(T entity);              // 提交
    Task<ApiResult> UnSubmit(T entity);            // 撤单
    Task<ApiResult> Enable(T entity);              // 启用
    Task<ApiResult> UnEnable(T entity);            // 停用
    Task<ApiResult> Import(ImportData entity);     // Excel 导入
    Task<ApiResult> Export(SysSingleTableDTO entity);
    Task<ApiResult> Post(T entity);                // 过账
    Task<ApiResult> UnPost(T entity);              // 反过账
    Task<ApiResult> Close(T entity);               // 结案
    Task<ApiResult> UnClose(T entity);
    Task<ApiResult> DoAction(T entity);            // 通用动作入口
    Task<ApiResult> CommonQuery(SysSingleTableDTO dto);   // 通用查询
}
```

**注意动作是成对的**（Audit/UnAudit、Submit/UnSubmit、Post/UnPost、Close/UnClose、
Enable/UnEnable）。配对是刻意的 —— 权限要按动作配对授予，审计日志要能还原「谁什么时候反审核了」。

## 3. 门面 BaseOperateServices

`[实现: Proj.Base/Proj.Base.Services/BaseServices/BaseOperateServices.cs:15-64]`：

```csharp
public class BaseOperateServices<T> : BaseOperate<T>, IBaseOperateServices<T> where T : class
{
    private readonly IAddOperate<T> addOperate;
    private readonly IUpdateOperate<T> updateOperate;
    private readonly IDeleteOperate<T> deleteOperate;
    private readonly IQueryOperate<T> queryOperate;
    private readonly ICloseOperate<T> closeOperate;
    private readonly IEnableOperate<T> enableOperate;
    private readonly IportOperate<T> iportOperate;
    private readonly IPostOperate<T> postOperate;
    private readonly ISubmitOperate<T> submitOperate;
    private readonly IAuditOperate<T> auditOperate;

    public BaseOperateServices(ILogger<BaseOperateServices<T>> logger, IBaseRepository<T> BaseDal,
        IAddOperate<T> addOperate, IUpdateOperate<T> updateOperate, IDeleteOperate<T> deleteOperate,
        IQueryOperate<T> queryOperate, ICloseOperate<T> closeOperate, IEnableOperate<T> enableOperate,
        IportOperate<T> iportOperate, IPostOperate<T> postOperate, ISubmitOperate<T> submitOperate,
        IAuditOperate<T> auditOperate) : base(logger, BaseDal)
    {
        // ... 逐个赋值

        dataFormMst = DevelopHelper.GetDataFormMst(typeof(T).Name.Replace("DTO", ""));
    }

    private T_META_DataFormMst dataFormMst;
    T_META_DataFormMst IBaseOperateServices<T>.dataFormMst { get => dataFormMst; set { } }

    public Task<ApiResult> Add(T entity)   => addOperate.Add(entity, dataFormMst);
    public Task<ApiResult> Audit(T entity) => auditOperate.Audit(entity, dataFormMst);
    // ... 每个动作都是「把元数据交给对应策略」
}
```

**这是门面模式的教科书用法**：11 个策略实例注进来，每个方法只做一件事 ——
把 `dataFormMst` 连同实体交给该动作的策略。门面自己不写任何逻辑。

**元数据在构造函数里加载**。因为注册是 `InstancePerDependency`（见 §7），
每次解析都会跑一遍 —— 好在 `DevelopHelper` 有 Redis 缓存，命中时只是一次反序列化。
但**没命中时要查三次库**（主表 + 接口 + 字段），所以第一波请求会慢。

⚠️ 注意 `dataFormMst` 属性的显式接口实现 `set { }` 是**空 setter** ——
外部无法替换元数据。这是刻意的（防止业务层篡改元数据），但也意味着**没法在测试里注入假元数据**。

## 4. Add 策略：元数据驱动的主循环

`[实现: Proj.Base/Proj.Base.Services/BaseServices/Implement/AddOperate.cs:139-222]`，
去掉注释后的有效代码：

```csharp
public virtual async Task<ApiResult> Add(TEntity entity, T_META_DataFormMst dataFormMst)
{
    _entity = entity;
    _dataFormMst = dataFormMst;

    ApiResult result = new ApiResult();
    try
    {
        dynamic argModel = entity;
        DateTime currentTime = Db.Ado.GetDateTime("SELECT GETDATE();");   // 数据库时间，不是应用时间

        foreach (var formInfo in dataFormMst.lstInterMst.OrderBy(o => o.fUILevel))
        {
            // ① 从元数据算出这一层要忽略哪些列
            var primaryKeyFields   = formInfo.lstInterFields.Where(o => o.fIfPrimaryKey == true)
                                        .Select(o => o.fFieldCode).ToList();
            var primaryKeyIdentity = formInfo.lstInterFields.Where(o => o.fIfPrimaryKey == true && o.fType == "Identity")
                                        .Select(o => o.fFieldCode).ToList();
            var updatePks = formInfo.lstInterFields
                                .Where(o => o.fIfPrimaryKey != true && o.fIfUpToSql == true)
                                .Select(o => o.fFieldCode).ToArray();
            var unUpdatePks = formInfo.lstInterFields
                                .Where(o => o.fIfUpToSql == false || o.fType == "Identity")
                                .Select(o => o.fFieldCode).ToArray();

            if (formInfo.fDtoVar != "Mst")          // ② 明细层
            {
                var property = argModel.GetType().GetProperty(formInfo.fDtoVar);   // "lstItem1"
                var fatherForm = dataFormMst.lstInterMst
                    .FirstOrDefault(o => o.fInterCode == formInfo.fParent);

                if (property != null)
                {
                    var item = property.GetValue(argModel);
                    if (item != null && item.Count > 0)
                    {
                        BaseOperateHelper.CommonLogic(formInfo, fatherForm, item);   // 回填父主键
                        formInfo.fData = await Db.Insertable(item).AS(formInfo.fDBTable)
                            .IgnoreColumns(primaryKeyIdentity.ToArray())
                            .IgnoreColumns(unUpdatePks)
                            .ExecuteReturnEntityAsync();
                    }
                }
            }
            else                                     // ③ 主表层
            {
                var Mst = argModel.Mst;
                bool isUpdate = (updatePks.Length > 0);
                BaseOperateHelper.UpdateStatus(Db, entity, dataFormMst, EventEnum.Add, isUpdate);

                // 单据编号：配了 fReceiptCode 才自动生成
                if (!string.IsNullOrEmpty(dataFormMst.fReceiptCode) && primaryKeyFields.Count > 0)
                    BaseOperateHelper.GenerateNewReceiptNo(Db, dataFormMst.fReceiptCode, Mst, primaryKeyFields[0]);

                if (updatePks.Length > 0
                    && updatePks.Where(o => !BaseOperateHelper.excludedFields.Contains(o)).ToArray().Length > 0)
                {
                    formInfo.fData = await Db.Insertable(Mst).AS(Mst.GetType().Name)
                        .IgnoreColumns(primaryKeyIdentity.ToArray())
                        .IgnoreColumns(unUpdatePks)
                        .ExecuteReturnEntityAsync();
                }

                if (formInfo.fData == null) formInfo.fData = Mst;
                result.response = formInfo.fData;    // 回传保存后的实体（带自增主键）
            }
        }

        result.success = true;
        result.msg = _localizer["Success"];
    }
    catch (Exception ex)
    {
        result.success = false;
        result.msg = ex.Message;          // ← 关键：不 throw
    }
    return result;
}
```

### 几个必须理解的细节

**① `OrderBy(o => o.fUILevel)`** —— 主表先插，明细后插。因为明细要拿主表的自增主键。
配错 `fUILevel` 的直接后果是**明细表的外键全是 0**。

**② 主表明细的表名来源不一致。**

```csharp
// 明细：用配置里的 fDBTable
Db.Insertable(item).AS(formInfo.fDBTable)

// 主表：用实体类名
Db.Insertable(Mst).AS(Mst.GetType().Name)
```

分析：主表这里用类名而不是 `fDBTable`，靠的是 `[SugarTable("T_Sample_Master")]`
和类名恰好同名的约定。**一旦有人把 `[SugarTable]` 写成跟类名不同的名字，主表插入就会写错表** ——
而且因为 `.AS()` 覆盖了 SugarTable，它不会报错，会直接插到一张叫类名的表里（大概率不存在）。

复刻时统一成 `formInfo.fDBTable`。

**③ `IgnoreColumns(primaryKeyIdentity)`** —— 自增列不参与插入。这就是为什么
`fType == "Identity"` 必须配对：**漏配的话自增主键会被显式插成 0**，
SQL Server 报 `Cannot insert explicit value for identity column` 或者插进一堆 0。

**④ `IgnoreColumns(unUpdatePks)`** —— `fIfUpToSql == false` 的字段不写库。
这是「只读字段」「前端展示字段」「计算字段」的实现方式。

**⑤ `BaseOperateHelper.UpdateStatus`** —— 统一填 `fCDate` / `fCreator` / `fCUserCode` 等标准字段。
单据的「谁建的、什么时候建的」靠它，不靠每个模块自己写。

**⑥ `BaseOperateHelper.excludedFields`** —— 一个静态的「永不写入」字段名单。
命名是反的（叫 excluded 但用的是 `!Contains`），读代码时注意。

**⑦ `Db.Ado.GetDateTime("SELECT GETDATE();")`** —— 取**数据库服务器**时间。
多应用实例部署时这是正确的选择（避免机器时钟不一致）；代价是每次 Add 多一次往返。
注意 `currentTime` 变量取了但**在主逻辑里没被用到**（只在已注释的 `AddNew` 里用）——
分析：这是一处死变量，可以在复刻时删掉。

## 5. 异常与事务的配合

把 04 和 [02](02-plugin-and-aop.md) 的 [`Trans`](02-plugin-and-aop.md#5-事务边界) 连起来看：

```
业务方法抛异常
  → 策略实现 catch 住，返回 {success:false, msg:...}     ← AddOperate.cs:215-219
    → 回到 Trans：if (!result.success) RollbackTran()   ← CusProxyGenerator.cs:299-306
      → 返回给前端 {success:false, msg:"..."}
```

**策略实现把异常吞了，所以 `Trans` 的 `catch` 分支实际上很少走到。**
回滚是靠 `!result.success` 这一条路触发的。

这意味着：**如果你自己写一个策略实现，忘了 catch，异常会穿透到 `Trans` 的 catch ——
那条路也会回滚，但异常会 `throw` 出去**，最终被 `CusProxyGeneratorNew.Invoke` 的 catch 接住
变成 `result.msg`。两条路都能回滚，但**消息形态不同**（后者只有 message，没有前面的 `___` 拼接）。

**对复刻的启示**：不要依赖「异常一定被吞掉」这个假设来写业务代码。
统一约定「策略层 catch 并转 ApiResult」，异常只是最后一道网。

## 6. 缓存策略实例

`InstancePerDependency` + 每次解析都加载元数据，意味着**同一个请求里如果解析两次
`IBaseOperateServices<T>`，元数据会被加载两次**。实际中控制器只解析一次，所以还好。

如果要做优化，正确的做法**不是**把它改成单例 —— 因为元数据是按账套变的
（见 [03 §3](03-metadata.md#3-加载与缓存)）。应该改成 `InstancePerLifetimeScope`，
让同一个请求共享一份。

## 7. DI 注册

`[实现: Proj.Base/Proj.Base.Extensions/ServiceExtensions/AutofacModuleRegister.cs]`：

```csharp
builder.RegisterType<LogAOP>().Named<IInterceptor>("business-service log");

builder.RegisterGeneric(typeof(BaseRepository<>)).As(typeof(IBaseRepository<>)).InstancePerDependency();
builder.RegisterGeneric(typeof(BaseServices<>)).As(typeof(IBaseServices<>)).InstancePerDependency();

builder.RegisterGeneric(typeof(BaseOperate<>)).As(typeof(ICommOperate<>))
    .InstancePerDependency().EnableInterfaceInterceptors();

builder.RegisterGeneric(typeof(AddOperateImpl<>)).As(typeof(IAddOperate<>))
    .InstancePerDependency().EnableInterfaceInterceptors();
// ... AuditOperatImpl / CloseOeprateImpl / DeleteOperateImpl / EnableOperateImpl / PortOperateImpl
//     PostOperateImpl / QueryOperateImpl / SubmitOperateImpl / UpdateOperateImpl

builder.RegisterGeneric(typeof(BaseOperateServices<>)).As(typeof(IBaseOperateServices<>))
    .InstancePerDependency().EnableInterfaceInterceptors();
builder.RegisterGeneric(typeof(BusinessBase<>)).As(typeof(IBusinessBase<>))
    .InstancePerDependency().EnableInterfaceInterceptors();

builder.RegisterAssemblyTypes(assemblysServices).AsImplementedInterfaces()
    .InstancePerDependency().PropertiesAutowired().EnableInterfaceInterceptors();

builder.RegisterType<UnitOfWorkManage>().As<IUnitOfWorkManage>()
    .AsImplementedInterfaces().InstancePerDependency().PropertiesAutowired();

builder.RegisterModule(new BusinessModule());   // BusinessProvider -> IBusinessProvider
```

**全是泛型注册** —— 这是为什么引擎能对 `MOD001_SampleDTO` 一无所知却仍然服务它。
开放泛型 `BaseRepository<>` 在解析时由容器闭合，不需要为每个 DTO 写一行注册。

`EnableInterfaceInterceptors()` 是 Autofac 的接口代理开关
`[联网-官方: https://docs.autofac.org/en/stable/advanced/interceptors.html]`，
官方文档给出两个必须知道的约束：

1. **接口必须是 public**（或对动态代理程序集可见）。`internal` 接口需要加
   `[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]`。
2. 官方明确写了：**Castle 拦截器只提供同步机制，没有原生 async/await 支持。**

这就是为什么在这套架构里，`Base.*` 层的 AOP（Autofac/Castle）和业务层的 AOP
（DispatchProxy）是**两套独立机制** —— Castle 拦不住接口里的 `Task<ApiResult>` 方法
的异步语义，所以业务层自己用 `DispatchProxy` 重做了一遍。

## 8. 已知问题

**`Proj.Base.Services/BASE/BaseOperateServices.cs` 是一个 800+ 行、全部注释掉的旧实现。**
它和 `BaseServices/BaseOperateServices.cs` 同名不同目录 —— 一个废弃、一个在用。
**复刻时直接不要这个文件。** 保留它的唯一后果是让人 `Ctrl+F` 找 `AddOperateServices` 时
先翻到 800 行注释。

同理，`AddOperate.cs` 里从第 42 行到 135 行是一整段注释掉的 `AddNew`，
以及文件末尾大段注释掉的 `BeforeProcess` / `AfterProcess`。
这些是**从 Delphi 风格的过程式代码迁移过来的痕迹**（`CheckTblFldInput`、
`CommonFun.getObjPropertyValue` 这些命名不是 C# 风格的）。

**复刻时只保留 `Add` 这一个方法**，历史痕迹全部不要。

继续读 [05-data-and-tenant.md](05-data-and-tenant.md)。
