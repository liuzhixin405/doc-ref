# 02 · 动态装载与 AOP 代理

> 依据标注同 [01](01-scaffold.md)：`[实现: 路径]` = 取自本仓库；`[来源: id p页码]` = 本地官方 PDF；
> `[联网-官方: URL]` = L1；`[联网-非官方: URL]` = L2；**「分析」= 我的推断，不是文档结论**。

## 目录

- [1. 两段式装载](#1-两段式装载)
- [2. BusinessProvider：按需装载业务类](#2-businessprovider按需装载业务类)
- [3. DispatchProxy 代理](#3-dispatchproxy-代理)
- [4. AOP 链的执行语义](#4-aop-链的执行语义)
- [5. 事务边界](#5-事务边界)
- [6. 特性契约](#6-特性契约)
- [7. 热重载](#7-热重载)
- [8. 这个设计的代价](#8-这个设计的代价)

---

## 1. 两段式装载

**程序集装载发生在两个完全不同的时刻**，理解这一点是理解整个架构的关键：

| 时机 | 装什么 | 谁装的 | 频率 |
|---|---|---|---|
| 启动时 | `*.Services.dll`（控制器） | `Program.cs` 的 `ApplicationPartManager` | 每进程一次 |
| 每次控制器构造 | `*.Business.dll`（业务类） | `BusinessProvider.GetBusinessNew<T>` | 每请求一次 |

**为什么分开**：控制器必须让 ASP.NET Core 的路由系统在启动时就枚举出来，否则路由表建不起来。
而业务类只有在真的要执行动作时才需要 —— 而且**多数请求根本不会走到业务类**
（比如 `CommonQuery` 打开列表页，走的是 `BaseOperateServices` 的通用查询）。

## 2. BusinessProvider：按需装载业务类

`[实现: Proj.Base/Proj.Base.Reflect/IBusinessProvider.cs]`：

```csharp
public interface IBusinessProvider
{
    IBusiness<T> GetBusiness<T>() where T : class;      // 旧版，用 ICusAop，实际已废弃
    IBusiness<T> GetBusinessNew<T>() where T : class;   // 现行版本
}

public class BusinessProvider : IBusinessProvider
{
    public IBusiness<T> GetBusinessNew<T>() where T : class
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "lib");
        var serviceName = typeof(T).Name.Replace("DTO", "");
        string path = Path.Combine(folder, $"{serviceName}.Business.dll");

        var assembly = Assembly.LoadFrom(path);
        var businessType = assembly.GetTypes()
            .Where(t => typeof(IBusiness<T>).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .FirstOrDefault();

        var cusAops = assembly.GetType().Assembly.GetTypes()
            .Where(t => typeof(IBusinessAop).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .Select(t => (IBusinessAop)Activator.CreateInstance(t)).ToList();

        if (businessType != null)
        {
            var instance = (IBusiness<T>)Activator.CreateInstance(businessType);
            return CusProxyGeneratorNew<T>.Create(instance, cusAops);
        }
        else
        {
            throw new InvalidOperationException($"Unable to find suitable types in the assembly '{path}'.");
        }
    }
}
```

四个要点：

1. **DLL 路径由 DTO 类型名推导**：`typeof(T).Name.Replace("DTO","")` + `.Business.dll`。
   DTO 叫 `MOD001_SampleDTO`，就找 `MOD001_Sample.Business.dll`。
2. **AOP 类从同一个 DLL 里扫**：`assembly.GetTypes().Where(t => typeof(IBusinessAop).IsAssignableFrom(t))`。
   所以 **AOP 类必须和业务类同项目** —— 放到 `Base.*` 里不会被发现。
3. **AOP 实例是 `Activator.CreateInstance` 造的**，不是 DI 容器给的。
   所以 AOP 类**不能靠构造函数注入**，只能 `App.GetService<T>()` 或自建 scope 拿依赖。
4. **`GetBusiness<T>()`（旧版）是死代码**。它在同一个文件里，用 `ICusAop` 而非 `IBusinessAop`，
   没有任何调用方。复刻时**不要照抄**，只留新版。

## 3. DispatchProxy 代理

`DispatchProxy` 是 BCL 自带的动态代理基类
`[联网-官方: https://learn.microsoft.com/dotnet/api/system.reflection.dispatchproxy]`，
官方文档给出的约束正好解释了这段代码为什么这样写：

- `Create<T, TProxy>()` 里 **`T` 必须是接口**（是 class 就抛异常）—— 所以被代理的是
  `IBusiness<T>` 这个接口，不是 `SAL009_...Business` 这个类。
- **`TProxy` 必须继承 `DispatchProxy`、不能 sealed、必须有公共无参构造函数** ——
  这就是为什么 `CusProxyGeneratorNew<T>` 是 `public class` 且没有任何显式构造函数。
- `Invoke(MethodInfo, Object[])` 的文档描述是：
  「Whenever any method on the generated proxy type is called, this method is invoked to dispatch control.」
  —— 代理上**任何**方法调用都会进 `Invoke`，没有例外。所以 `Invoke` 里必须处理所有方法，
  包括你可能不关心的。

创建入口 `[实现: Proj.Base/Proj.Base.Reflect/CusProxyGenerator.cs:330-341]`：

```csharp
public static IBusiness<T> Create(IBusiness<T> business, List<IBusinessAop> cusAop)
{
    object proxy = Create<IBusiness<T>, CusProxyGeneratorNew<T>>();
    ((CusProxyGeneratorNew<T>)proxy).SetParameters(business, cusAop);
    return (IBusiness<T>)proxy;
}

private void SetParameters(IBusiness<T> business, List<IBusinessAop> cusAop)
{
    this.business = business;
    this.cusAop = cusAop;
}
```

`Create<T, TProxy>()` 返回的实例构造完后是「空」的，所以要 `SetParameters` 把真身塞进去 ——
这是 `DispatchProxy` 的标准用法。

## 4. AOP 链的执行语义

`[实现: Proj.Base/Proj.Base.Reflect/CusProxyGenerator.cs:198-274]`，精简后：

```csharp
protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
{
    BusinessAopContext aopContext = new();
    aopContext.Parameters = args;

    string methodKey = targetMethod.Name;
    if (!_cache.ContainsKey(methodKey))
    {
        var classType = business.GetType();
        var actionAttributes = classType.GetMethod(targetMethod.Name)
            .GetCustomAttributes<CusActionAttribute>().ToList();
        _cache[methodKey] = new MethodInfoCache()
        {
            Name = methodKey,
            ClassType = classType,
            //除了查询和导出接口均走事务逻辑
            UseTrans = (methodKey == "CommonQuery" || methodKey == "Export") ? false : true,
            ActionAttributes = actionAttributes
        };
    }
    var methodInfoCache = _cache[methodKey];
    ApiResult result = new ApiResult();

    if (methodInfoCache.UseAop)
    {
        var actionnames = methodInfoCache.ActionAttributes.Select(x => x.Name).ToList();
        var waitInvokes = cusAop
            .Where(x => actionnames.Contains(x.GetType().Name))
            .OrderBy(x => actionnames.IndexOf(x.GetType().Name)).ToList();   // 排序
        try
        {
            foreach (var item in waitInvokes)
                AsyncHelper.RunSync(async () => { await item.BeforeExecute(aopContext); });

            // 全部 Before 跑完，才统一判断是否短路
            if (((ApiResult)aopContext.BeforeResult).success == false)
                return Task.FromResult(aopContext.BeforeResult);

            var taskResult = methodInfoCache.UseTrans ? Trans(targetMethod, args)
                                                      : targetMethod.Invoke(business, args);
            if (taskResult is Task<ApiResult> task)
                result = AsyncHelper.RunSync(async () => await task)
                         ?? throw new Exception($"business method:{methodKey} invoke failed");
            else
                result = (ApiResult)taskResult;
            aopContext.Result = result;

            foreach (var item in waitInvokes)
                AsyncHelper.RunSync(async () => await item.After(aopContext));
        }
        catch (AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.Flatten().InnerExceptions)
                result.msg += innerException.Message + " ___ ";
        }
        catch (Exception ex) { result.msg = ex.Message; }

        return Task.FromResult(result);
    }
    else
    {
        return methodInfoCache.UseTrans ? Trans(targetMethod, args) : targetMethod.Invoke(business, args);
    }
}
```

必须理解的五条语义：

**① 顺序 = 特性声明顺序。** `[CusAction(nameof(A))] [CusAction(nameof(B))]` 就先 A 后 B。
靠 `OrderBy(x => actionnames.IndexOf(x.GetType().Name))` 实现 ——
**匹配键是 AOP 的类名**，所以特性里必须写 `nameof(CheckAddAop)`，写错一个字符就静默不执行。

**② Before 全部跑完才判短路。** 不是遇到失败就停 —— 循环走完，再统一看 `BeforeResult.success`。
如果你有多个 `[CusAction]`，第一个失败了，第二个的 `Before` **仍然会执行**。
写 AOP 时不能假设「我前面的校验失败了我就不用跑了」。

**③ After 没有短路。** `After` 里返回 `success=false` 不会中断后续 `After`，也不会回滚 ——
那时 `Trans` 早就 commit 了。**回滚只能靠 `Before` 短路或抛异常。**

**④ `BeforeResult` 必须由 AOP 自己赋值。** 见下面 `BusinessBaseAop` —— 基类的
`BeforeExecute` 会在 `Before` 返回后补一个 `success=true` 的默认值，所以不赋值不会 NRE。

**⑤ 异常被吞成 `result.msg`。** 业务方法抛异常时，`Invoke` catch 住，把消息塞进 `result.msg`
然后**返回一个 success 默认 false 的 ApiResult**。所以业务方法里的异常不会变成 500，
而是变成一条 `{success:false, msg:"..."}` —— 这解释了为什么这个系统的接口错误都长得一样。

### AOP 基类

`[实现: Proj.Base/Proj.Base.Reflect/BusinessBaseAop.cs]`：

```csharp
public abstract class BusinessBaseAop : IBusinessAop
{
    public virtual Task After(BusinessAopContext context)
    {
        context.AfterResult = new Model.ApiResult();
        context.AfterResult.success = true;
        return Task.CompletedTask;
    }

    public Task BeforeExecute(BusinessAopContext context)
    {
        Before(context);
        if (context.BeforeResult == null)
        {
            context.BeforeResult = new Model.ApiResult();
            context.BeforeResult.success = true;
        }
        return Task.CompletedTask;
    }

    public abstract Task Before(BusinessAopContext context);
}
```

**注意 `BeforeExecute` 不是 `virtual`，`Before` 才是抽象方法。** 所以业务 AOP 只实现
`Before`，`After` 按需 override。这个倒置是刻意的：`BeforeExecute` 承担「兜底默认值」这个
模板方法职责，不让子类覆盖掉。

### 上下文

`[实现: Proj.Base/Proj.Base.IServices/IBusiness/IBusinessAop.cs]`：

```csharp
public class BusinessAopContext
{
    public object[] Parameters { get; set; }   // == args，Before 里改它等于改传给业务方法的实参
    public ApiResult BeforeResult { get; set; }
    public ApiResult AfterResult { get; set; }
    public ApiResult Result { get; set; }      // 业务方法执行完才有值
    public object[] Others { get; set; }
}

public interface IBusinessAop
{
    Task BeforeExecute(BusinessAopContext context);
    Task After(BusinessAopContext context);
}
```

`Parameters` 直接就是 `args` 数组的引用 —— AOP 里 `((Dto)ctx.Parameters[0]).Mst.fSrcType = "10"`
这样的赋值**会真的传进业务方法**。这是本架构里补全字段最常用的手法。

## 5. 事务边界

`[实现: Proj.Base/Proj.Base.Reflect/CusProxyGenerator.cs:276-328]`：

```csharp
private async Task<ApiResult> Trans(MethodInfo? targetMethod, object?[]? args)
{
    var _unitOfWorkManage = App.GetService<IUnitOfWorkManage>();
    try
    {
        if (_unitOfWorkManage.TranCount <= 0)
            _unitOfWorkManage.BeginTran();

        var result = await (Task<ApiResult>)targetMethod.Invoke(business, args);

        if (!result.success)
        {
            _unitOfWorkManage.RollbackTran();
            return result;
        }
        if (_unitOfWorkManage.TranCount > 0)
            _unitOfWorkManage.CommitTran();
        return result;
    }
    catch (Exception e)
    {
        _unitOfWorkManage.RollbackTran();
        throw;
    }
    finally
    {
        _unitOfWorkManage.CloseConnection();
    }
}
```

事务判定语义（**注意与 SKILL.md 坑 1 呼应**）：

- `TranCount <= 0` 才 `BeginTran` —— 支持嵌套调用，靠计数器而非保存点
- `result.success == false` → 回滚。**业务用返回值表达失败，用异常也行，两条路都通**
- `finally` 里 `CloseConnection()` 会把 `TranCount` 归零

`UseTrans` 的判定是**硬编码**的：除 `CommonQuery` 和 `Export` 外全开事务。
`CusTransAttribute` / `CusNoTransAttribute` 被读进了局部变量 `classHasTransTrribute`，
但**从没参与运算**（源码里那行算 `UseTrans` 的代码被注释掉了）。

**复刻时的选择**：要么把特性真的接上（取消注释那行并补全 `methodHastransAttribute` /
`methodNoTransAttribute`），要么删掉这两个特性类。留着一个读了但不生效的装饰，
下一个人会花半天去查「为什么我标的 `[CusNoTrans]` 没用」。

## 6. 特性契约

`[实现: CustomAttribute/CusActionAtttibute.cs]`：

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class CusActionAttribute : Attribute
{
    private string name;
    public string Name { get { return name; } }
    public CusActionAttribute(string name) { this.name = name; }
}

public interface ICusAop
{
    Task<object?> Before(object[] parameters);
    Task<object?> After(object[] parameters);
}
```

`[实现: CustomAttribute/CusTransAttribute.cs]` / `CusNoTransAttribute.cs` —— 两个空特性，
`[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]`。

`AllowMultiple = true` 是必需的（一个方法挂多个 AOP），`Inherited = false` 意味着
**子类不会继承父类方法上的 AOP 标注**。

## 7. 热重载

`[实现: Proj.Api/BackService/LibFileWatcherService.cs]` 用 `FileSystemWatcher` 盯
`{CurrentDirectory}/lib`，变化时（`Counter % 3 == 0`，避抖动）触发**进程重启**：

```csharp
// 注册 ApplicationStopped 回调，在优雅关闭后重新拉起自己
Process.Start(new ProcessStartInfo {
    FileName = "dotnet",
    Arguments = $"exec \"{Assembly.GetEntryAssembly().Location}\""
});
```

**这是「重启」不是「热重载」。** 名字叫 `LibFileWatcher` 容易让人误以为是卸载旧程序集、
装载新的 —— 不是。原因见 [§8](#8-这个设计的代价)：程序集装进去就出不来，
想让新版本生效只能换进程。

还要注意 `Assembly.GetEntryAssembly().Location` 在**单文件发布**下返回空字符串，
这条重启路径会失效。用单文件发布就要改成 `Environment.ProcessPath`（分析，未查文档核实）。

## 8. 这个设计的代价

**① 程序集不可卸载。** `Assembly.LoadFrom` 把程序集装进默认 `AssemblyLoadContext`，
官方文档明确推荐改用 `AssemblyLoadContext` 的重载
`[联网-官方: https://learn.microsoft.com/dotnet/api/system.reflection.assembly.loadfrom]`。
而 `AssemblyLoadContext` 要能卸载，需要两个条件
`[联网-官方: https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblyloadcontext.unload]`：

- 该 ALC 必须是 **collectible**（`IsCollectible`）
- **不能存在对它的引用**（这是实际中最难满足的：一个静态字段、一个 GC handle、
  一个栈槽都会让它活着）

本架构 `BusinessProvider` 每次请求都 `Assembly.LoadFrom`，但**同 identity 的程序集只会真正加载一次**，
之后返回缓存的那个 —— 所以不是每次请求都泄漏，是每个模块泄漏一份且永不释放。模块数固定时
可以接受；要支持「不停机换模块」就必须上 collectible ALC。

**② `AsyncHelper.RunSync` 是 sync-over-async。** `DispatchProxy.Invoke` 是同步方法，
而 `IBusinessAop.BeforeExecute` / `After` 是异步的，所以必须阻塞等待。
Autofac 官方文档在讲 Castle 拦截器时也点出了同一件事：**Castle 拦截器只提供同步机制，
没有原生的 async/await 支持** `[联网-官方: https://docs.autofac.org/en/stable/advanced/interceptors.html]`。

分析：`RunSync` 会占用一个线程池线程去等另一个线程池线程，高并发下可能线程池饥饿。
当前实现里 `Trans` 是在 `Invoke` **外面** await 的（`AsyncHelper.RunSync(async () => await task)`），
所以真正耗时的 IO 不在 `RunSync` 的阻塞段内 —— 阻塞的是 AOP 钩子和一个 `Task` 的等待。
如果 AOP 里写了同步的远程调用，这个风险会放大。

**③ 缓存 key 是方法名。** `_cache[methodKey]` 用 `targetMethod.Name` 做键 ——
`IBusiness<T>` 的方法没有重载，所以安全。但**如果将来给接口加了重载方法，缓存会串**。

继续读 [03-metadata.md](03-metadata.md)。
