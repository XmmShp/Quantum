# 官方插件开发指南

## 为什么只有页面还不够

Quantum 的插件页面与 RPC 能力是两条独立的链路。Razor 页面能正常显示、能通过 DI 调用应用服务、能读写数据库，并不代表 Quantum 能发现或调用这些业务接口。

**凡是需要让 Quantum、其他插件或 AI 工具读取和调用的业务能力，都必须通过 NOF RPC 暴露。** 仅添加 `public` 方法、注册应用服务、在 manifest 中声明 UI 路由，均不会把方法加入 RPC 目录。

## 默认结构：Client → Handler → Domain / Persistence

官方插件默认使用 RPC Handler 作为应用层用例入口。页面通过生成的 `I…RpcServiceClient` 调用，Handler 直接依赖 `IRepository<T>`、`IDbContext` 和必要的基础设施。不要再建立一组与 RPC 一一对应的 ApplicationService / Session 转发层，也不要让页面直接读写仓储或调用 Handler。

```text
Razor 页面 → 生成的本地 RPC Client ──┐
                                    ├→ Handler → 领域对象 / 仓储 / DbContext
其他插件 / AI → Quantum RPC 调度 ───┘
```

Handler 负责查询、用例编排、关联校验、提交以及业务失败 `Result`。金额、日期范围、事项状态等领域不变量保留在领域对象中。重复的查询表达式、DTO 映射或单项规则可以提取为小型辅助函数；不要将整个 CRUD 用例重新收集到另一个 Service 类。

有真实生命周期的基础设施可以保留。例如 Codex 的 `CodexRuntime` 必须持有 App Server 进程、会话、流式消息、同步锁和取消令牌，这些状态不能跟随请求 Handler 创建和销毁。Handler 调用该运行时；页面的读取、连接、模型切换、发送和断开仍通过 RPC Client。页面只订阅 `ICodexRuntimeEvents.Changed` 通知，再通过 snapshot RPC 重新读取状态，不直接操作运行时。自动连接同样由 `ensure-auto-connected` RPC 进入。

## 从合约到宿主调用

以 [Ledger/Contract/ILedgerRpcService.cs](../Ledger/Contract/ILedgerRpcService.cs) 和 [Ledger/Application/LedgerRpcService.cs](../Ledger/Application/LedgerRpcService.cs) 为合约与服务声明示例：

```csharp
using System.ComponentModel;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;

public sealed record LedgerMonthRequest
{
    [Description("月份中的任意一天，yyyy-MM-dd，按整月查询。")]
    public required DateOnly Month { get; set; }
}

[Description("读取本地人民币账本的月度收支。")]
[TransportOverQuantum]
[RpcInvocationName("ledger")]
public interface ILedgerRpcService : IRpcService
{
    [Description("列出指定月份的全部收支记录。")]
    [RpcInvocationName("list")]
    Result<LedgerEntryDetails[]> ListEntries(LedgerMonthRequest request);
}

public partial class LedgerRpcService : RpcServer<ILedgerRpcService>;
```

1. 在插件项目中引用 `Quantum.Plugin.Abstraction`、`NOF.Application` 和 `NOF.Contract`，保持 NOF 源生成器启用。宿主需要生成的 `ServiceType`、`HandlerMappings` 和处理器类型；手写一个普通同名服务无法替代这些元数据。不要为了让构建通过而移除所需的生成器。
2. 合约继承 `IRpcService`，声明 `[TransportOverQuantum]`。为服务和方法设置稳定的 `[RpcInvocationName]`，避免重命名 CLR 类型时意外改变外部调用名。
3. 每个方法接收一个可 JSON 序列化的请求 DTO，返回 `Result` 或 `Result<T>`。不要把 `IServiceProvider`、数据库对象、CLR 类型名或文件系统句柄放进公开 DTO。
4. 从生成的 `LedgerRpcService.ListEntries` 等处理器基类派生实现。处理器直接实现用例、注入仓储与数据库、传递 `CancellationToken`，参数错误和未找到记录直接抛出 NOF `DomainException`，由 Quantum 配置的 NOF 入站管道转换为失败 Result；不吞掉取消异常。
5. 由 NOF 的 `RpcServerAutoInjectGenerator` 生成程序集初始化器，自动注册生成的处理器基类与具体实现。Quantum 加载插件时执行这些初始化器，插件不再逐项写 `AddTransient`。独立测试同样必须执行生成的初始化器。
6. 本地生成 Client 与 Quantum 宿主 RPC 调度都会为每次调用创建独立的服务端 scope，激活 daemon 服务并在调用结束后释放。Handler 直接使用注入的 scoped 仓储与数据库，不再额外创建 Session / scope。确需在其它场景自建 DI scope 时，立即调用 `scope.ServiceProvider.ResolveDaemonServices()`，遵循 NOF 生命周期要求。数据库提交应保持原子性，失败操作的跟踪状态不能泄漏到下一次保存。
7. 给服务、方法和请求属性写清楚 `DescriptionAttribute`。宿主会把这些说明与输入输出 JSON Schema 一起导出，供 Quantum 和 AI 工具理解用途、单位、枚举及限制。明确注明修改和删除等副作用。

宿主扫描插件入口程序集中的 NOF RPC 服务，读取上述元数据，只暴露带 `[TransportOverQuantum]` 的合约。以记账插件为例：

| 名称 | 用途 |
| --- | --- |
| `quantum.rpc.catalog` | 获取当前已激活的宿主及插件 RPC 目录、描述和 Schema |
| `quantum.plugin.ledger.ledger.list` | 带插件 ID 的稳定、完整调用名，集成代码优先使用 |
| `ledger.list` | 短调用名；多个插件重名时，宿主按插件 ID 的 ordinal 字典序选择 |

公开 RPC 不要求调用方额外声明 `dependencies` 或 `integrations`；这两项控制加载关系，不负责暴露 API。仍需先安装并激活提供方插件。插件停止后不应继续出现在可用能力目录中。

## 文件组织与代码风格

一个生产类型默认一个文件，Handler、请求与共享模型分别组织：

```text
Ledger/
  Contract/
    ILedgerRpcService.cs     # 带描述和稳定名称的 RPC 合约
    Requests/               # 请求 DTO
    Models/                 # 公共响应 DTO、枚举
  Application/
    LedgerRpcService.cs     # RpcServer<T> 生成入口
    Handlers/               # 每个用例一个文件
    LedgerMappings.cs       # 领域对象到 DTO 的映射
  Domain/                   # 持久化领域对象和业务规则
  LedgerModel.cs            # 数据库模型配置
  UI/
    Pages/
      Ledger.razor          # 页面标记
      Ledger.razor.cs       # 组件状态与交互
    Localization/           # 资源标记类与中英文 resx
```

Calendar、Codex 采用相同的 Contract/Application 划分。各插件的初始化器、数据库模型配置和运行时适配器直接放在插件根目录，不单独建立 Hosting 或 Infrastructure 文件夹。有生命周期的运行时适配器保留独立类型，文件直接放在插件根目录；仅供应用内部使用的模型保留在 Application/Models。Contract 只包含接口、数据结构和枚举，不引用领域实体，也不承担查询、映射或运行时逻辑。

Handler 使用 Primary Constructor 注入依赖；其它服务可使用普通构造函数和 `private readonly` 字段。Contract DTO 和数据 record 不声明构造函数，直接定义 `{ get; set; }` 属性：必须显式提供的成员标记 `required`，可选成员在属性上设置默认值，调用处使用成员初始化器。保留 record 的值相等及 `with` 语义。保留属性上的 `Description`、JSON 字段、类型、可空性和必填规则；通过属性初始化保留可选项省略时的运行时默认值。固定版本的 HostRpc 测试快照保留上游原貌。

请求及其专用响应可放在同一个 `*Request.cs` 文件中；跨用例复用的模型单独存放。重排目录时保留公开 Contract 的命名空间、RPC 名称与 JSON 结构。UI 命名空间跟随 UI 目录，移动时同步更新 manifest 中的页面和全局组件名称。通用的错误映射、重复查询和 DTO 映射可以保留，只有一层转发的简单查询包装直接内联；不要为每一行表达式创建辅助类。

Application、Domain、Infrastructure 和 Contract 不引用本地化服务或 UI 资源。内置业务错误、状态消息和 RPC 目录描述统一使用英文，结果不随调用线程的界面语言变化。UI 在接收结果后使用自己的资源做本地化；用户输入、模型回复及外部诊断内容按原文保留。通用 RPC 服务注册、可选能力发现和结果处理由 Quantum SDK 提供；插件不维护 Shared 目录，也不链接 SDK 源文件。

`.editorconfig` 统一 UTF-8、LF、C# 四空格缩进和文件范围命名空间。所有 `if` / `else` / 循环分支使用大括号，块的大括号独占行；`try` / `catch` / `finally` 展开书写。简单表达式可以内联，复杂页面逻辑放在同名 `.razor.cs` 中。

## 实体映射与查询投影

实体到 DTO 的转换使用 NOF 的表达式映射。字段同名且语义相同的映射由生成器注册，例如 Calendar：

```csharp
[Mappable<CalendarEntry, CalendarEntryDetails>]
internal static partial class CalendarMappings;
```

读查询先完成筛选、排序和分页，再调用 `.ProjectTo<CalendarEntryDetails>().ToArrayAsync(cancellationToken)`。不要先物化完整实体再做字段转换，也不要在 `IQueryable.Select` 里调用 `mapper.Map`。已在内存中的新建或更新实体使用 `Mapper.Current.Map<CalendarEntry, CalendarEntryDetails>(entry)`，复用同一份表达式。Quantum 在请求 scope 中解析 NOF daemon 服务，为 `Mapper.Current` 和无参 `ProjectTo` 建立上下文；独立测试也可显式传入 `IMapper`。

需要计算或字段转换时，使用 `services.AddMapping<TSource, TDestination>(expression)` 注册自定义表达式。Ledger 将 `AmountInFen / 100m` 投影为金额，同时读取日历 ID 原始 JSON；投影完成后批量查询关联记录并解析 JSON，保持关联查询次数固定。不要把数据库无法翻译的 JSON 解码或服务调用混入 SQL 投影。SQLite 回归测试验证实际 SQL、金额精度及关联数据。

映射必须在首次解析 `IMapper` 前完成注册。Quantum 添加持久化默认服务时保留插件初始化器已创建的 `MappingRegistry`。

## 页面调用与本地 Client 注册

`ILedgerRpcService : IRpcService` 是 RPC 描述合约，方法返回同步 `Result`，不直接作为可注入的执行实现。NOF 生成 `ILedgerRpcServiceClient : IRpcClient<ILedgerRpcService>`，方法为异步形式，并接收 `Context` 和 `CancellationToken`。引用 `NOF.Infrastructure` 并保留生成器后，`LedgerRpcService` 会生成 `LocalLedgerRpcServiceClient`。

官方插件通过 `NOF.Application` 的原生 `services.AddRpcServer<T>()` 注册 server registry 和 scoped server，无需 SDK 封装或 Host Builder。Quantum 框架负责 resolver、请求管道及异常中间件，保留 NOF 原生 Client 和 Handler 调度。该注册入口只适用于 .NET/NOF，Web 插件不需要注册本地 NOF 服务。
```csharp
services.AddRpcServer<LedgerRpcService>();
services.AddScoped<ILedgerRpcServiceClient, LocalLedgerRpcServiceClient>();
```

插件自有依赖使用组件构造函数注入，避免从宿主页面的 property injection provider 解析插件私有服务：

```csharp
private readonly ILedgerRpcServiceClient _ledger;
public Ledger(ILedgerRpcServiceClient ledger)
{
    _ledger = ledger;
}

private async Task LoadAsync(CancellationToken cancellationToken)
{
    var result = await _ledger.ListEntriesAsync(
        new LedgerMonthRequest { Month = _month }, Context.Empty, cancellationToken);
    if (!result.IsSuccess)
    {
        _error = result.Message;
        return;
    }
    _entries = result.Value!;
}
```

页面只持有请求/响应 DTO 与展示状态，不持有持久化实体。RPC 直接返回 NOF `Result` / `Result<T>`；页面检查 `IsSuccess`，失败时显示本地化消息并立即结束当前操作，成功后再读取 `Value`、清空表单、关闭对话框或显示成功消息。不把失败 Result 转成自定义 RPC 异常。取消继续向上传播。

本地 Client 进入 NOF 原生出入站管线，每次调用有独立数据库 scope；它不经过 Quantum 的 JSON 序列化和跨插件可用性检查。跨插件仍使用完整名称与宿主 `IRpcInvoker`，不要注入另一个插件的本地 Client，也不要把插件拥有的 CLR 对象传过运行时边界。两条调用路径必须分别测试。

## 发布前必须验证两件事

**能发现：** 调用 `quantum.rpc.catalog`，确认返回的 `services` 包含本插件 ID、服务名、全部预期方法、方法描述和请求 Schema。接口文件存在或处理器单元测试通过，不能替代这个检查。

**能调用：** 使用宿主 `IRpcInvoker`，按完整名称跨 JSON 边界调用，例如：

```csharp
var result = await invoker.InvokeAsync<LedgerEntryDetails[]>(
    "quantum.plugin.ledger.ledger.list",
    new { month = "2026-10-01" },
    Context.Empty,
    cancellationToken);
```

测试必须覆盖生成本地 Client 和宿主 JSON 边界两条路径。本地 Client 测试应复用同一 Client 多次调用、验证失败后可恢复、每次写入使用独立 DbContext，并确认取消不写入；Codex 等有状态基础设施还要验证页面 Client 与宿主 RPC 共享同一运行时。

测试应包括查询、新增、修改、删除、无效请求、缺失记录和取消。写入后应通过另一次 RPC 查询验证持久化结果，而不只是直接调用处理器或应用服务。对于导入，要验证重复导入和冲突时整次拒绝。

[记账 RPC 集成测试](../tests/Quantum.LedgerPlugin.Tests/LedgerRpcTests.cs) 使用真实 SQLite 和从 Quantum 宿主固定版本取出的 RPC 发现、目录、序列化及调度实现，实际扫描记账插件 DLL 并执行调用。宿主测试快照的来源和哈希记录在 [HostRpc/provenance.json](../tests/Quantum.LedgerPlugin.Tests/HostRpc/provenance.json)，升级宿主 ABI 或发现规则时应同步更新并重新验证。

运行：

```sh
dotnet restore Quantum.OfficialPlugins.slnx
dotnet format Quantum.OfficialPlugins.slnx --no-restore --exclude tests/Quantum.LedgerPlugin.Tests/HostRpc
dotnet build Quantum.OfficialPlugins.slnx -c Release --no-restore
dotnet test Quantum.OfficialPlugins.slnx -c Release --no-build --no-restore
```

按 restore → format → build → test 顺序执行，前一步成功后再继续。带固定哈希的 HostRpc 宿主源码快照不参与格式化。

本机 Windows 环境的 `dotnet` 命令必须在管理员进程中运行。生成器或编译器加载失败时先排查构建环境，不能通过删除 RPC 合约或移除所需生成器把问题隐藏掉。

发布时递增 `plugin.json` 的版本，保留已有 SQL 迁移历史。平台审核通过和页面可用都不能替代 RPC 目录与调用测试。

Quantum 在启动时统一配置 `JsonSerializerOptions.NOF`：枚举采用字符串 JSON 表示，并校验必填成员与可空性。插件无需声明枚举转换器。跨插件调用和本地 Client 均进入 NOF 入站管道，业务失败通过 `DomainException` 表达；非业务异常由管道捕获并返回通用错误，不影响后续请求。外部进程协议或持久化格式使用其自身的协议规则。

## SDK 与 NOF 依赖

原生 `IServiceCollection.AddRpcServer<T>()` 要求 NOF `11.0.0-nightly.1473609.75efa591` 或兼容的后续版本。宿主、SDK 和插件应统一 NOF 版本。插件使用已发布的 `Quantum.Plugin.Abstraction` 0.1.0 即可完成当前 RPC 联动和注册；不依赖 SDK 自定义注册或发现封装。

可选联动直接按 canonical RPC 能力名调用 `IRpcInvoker.InvokeAsync<T>`，由宿主完成路由，返回 NOF Result。页面通过既有 `quantum.rpc.catalog` RPC 逐项判断能力可用性；选择候选日程只要求 `calendar.list`，查看关联详情只要求 `calendar.get`，不要求同一提供者或完整能力组。目录检查仅用于 UI，实际调用仍须处理能力已卸载等失败。只有业务明确要求指定实现时才使用 qualified name。跨能力使用的资源 ID 和数据语义由契约约定。

## 导航图标

官方插件使用 `ui.routes[].iconFile` 引用 `wwwroot/icons/plugin.ico`，保留 `icon` 文字作为图像加载失败和旧移动客户端的回退。仅支持标准 ICO，包含 PNG 或 DIB 图层均可。发布包必须包含声明的 ICO，`scripts/verify-plugin-assets.py` 会检查文件是否随包发布。此 manifest 字段要求 Quantum 0.1.16 或更新版本。
