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

有真实生命周期的基础设施可以保留。例如 Codex 的 `Infrastructure/CodexRuntime` 必须持有 App Server 进程、会话、流式消息、同步锁和取消令牌，这些状态不能跟随请求 Handler 创建和销毁。Handler 调用该运行时；页面的读取、连接、模型切换、发送和断开仍通过 RPC Client。页面只订阅 `ICodexRuntimeEvents.Changed` 通知，再通过 snapshot RPC 重新读取状态，不直接操作运行时。自动连接同样由 `ensure-auto-connected` RPC 进入。

## 从合约到宿主调用

以 [Ledger/Rpc/ILedgerRpcService.cs](../Ledger/Rpc/ILedgerRpcService.cs) 和 [Ledger/Rpc/LedgerRpcService.cs](../Ledger/Rpc/LedgerRpcService.cs) 为合约与服务声明示例：

```csharp
using System.ComponentModel;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;

public sealed record LedgerMonthRequest(
    [property: Description("月份中的任意一天，yyyy-MM-dd，按整月查询。")] DateOnly Month);

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
4. 从生成的 `LedgerRpcService.ListEntries` 等处理器基类派生实现。处理器直接实现用例、注入仓储与数据库、传递 `CancellationToken`，将参数错误和未找到记录转换为明确的业务失败 Result，不吞掉取消异常。
5. 在 `IQuantumPlugin.ConfigureServices` 或 `IAssemblyInitializer.Initialize` 注册**生成的处理器基类 → 具体处理器**。例如：

   ```csharp
   services.AddTransient<LedgerRpcService.ListEntries, ListLedgerEntries>();
   ```

   每一个公开 RPC 方法都要有可解析的处理器；页面和外部调用统一进入这些处理器。
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
Ledger/Rpc/
  ILedgerRpcService.cs        # 带描述和稳定名称的 RPC 合约
  LedgerRpcService.cs         # RpcServer<T> 生成入口
  Handlers/                  # 每个用例一个文件
  Requests/                  # 请求 DTO
  Models/                    # 共享响应 DTO、枚举
Ledger/Pages/
  Ledger.razor               # 页面标记
  Ledger.razor.cs             # 组件状态与交互
```

请求及其专用响应可放在同一个 `*Request.cs` 文件中；跨用例复用的模型单独存放。重排目录时保留已有命名空间、RPC 名称与 JSON 结构。通用的错误映射、重复查询和 DTO 映射可以保留，只有一层转发的简单查询包装直接内联；不要为每一行表达式创建辅助类。

`.editorconfig` 统一 UTF-8、LF、C# 四空格缩进和文件范围命名空间。所有 `if` / `else` / 循环分支使用大括号，块的大括号独占行；`try` / `catch` / `finally` 展开书写。简单表达式可以内联，复杂页面逻辑放在同名 `.razor.cs` 中。

## 页面调用与本地 Client 注册

`ILedgerRpcService : IRpcService` 是 RPC 描述合约，方法返回同步 `Result`，不直接作为可注入的执行实现。NOF 生成 `ILedgerRpcServiceClient : IRpcClient<ILedgerRpcService>`，方法为异步形式，并接收 `Context` 和 `CancellationToken`。引用 `NOF.Infrastructure` 并保留生成器后，`LedgerRpcService` 会生成 `LocalLedgerRpcServiceClient`。

官方插件的 bootstrap 只接收 `IServiceCollection`，而 NOF 的 `builder.AddRpcServer<T>()` 接收 `IHostApplicationBuilder`。本仓库通过 [Shared/PluginRpcRegistration.cs](../Shared/PluginRpcRegistration.cs) 中的 `AddPluginRpcServer<T>()` 注册原生 NOF server registry、scoped server、resolver 和请求管线，沿用 NOF 原生 Client 和 Handler 调度。共享源文件通过各插件 csproj 的 `Compile Include="../Shared/*.cs"` 链入。

```csharp
services.AddPluginRpcServer<LedgerRpcService>();
services.AddScoped<ILedgerRpcServiceClient, LocalLedgerRpcServiceClient>();
services.AddTransient<LedgerRpcService.ListEntries, ListLedgerEntries>();
// 其余 Handler 同样注册。
```

插件自有依赖使用组件构造函数注入，避免从宿主页面的 property injection provider 解析插件私有服务：

```csharp
private readonly ILedgerRpcServiceClient _ledger;
public Ledger(ILedgerRpcServiceClient ledger) => _ledger = ledger;

private async Task LoadAsync(CancellationToken cancellationToken)
{
    var result = await _ledger.ListEntriesAsync(
        new LedgerMonthRequest(_month), Context.Empty, cancellationToken);
    if (!result.IsSuccess)
    {
        _error = result.Message;
        return;
    }
    _entries = result.Value!;
}
```

页面只持有请求/响应 DTO 与展示状态，不持有持久化实体。失败 `Result` 必须在清空编辑表单、关闭对话框、刷新列表或显示成功消息前处理。本仓库的 [Shared/RpcResults.cs](../Shared/RpcResults.cs) 提供 `ValueOrThrow` / `ThrowIfFailed`，页面在统一错误边界显示失败消息。取消继续向上传播。

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
dotnet format Quantum.OfficialPlugins.slnx --no-restore --exclude Shared tests/Quantum.LedgerPlugin.Tests/HostRpc
dotnet format Ledger/Quantum.LedgerPlugin.csproj --no-restore --include Shared
dotnet build Quantum.OfficialPlugins.slnx -c Release --no-restore
dotnet test Quantum.OfficialPlugins.slnx -c Release --no-build --no-restore
```

按 restore → format → build → test 顺序执行，前一步成功后再继续。共享源码在多个插件项目中链接，解决方案格式化排除 Shared 后，通过一个插件项目单独格式化，避免 Roslyn 的链接文件合并异常。带固定哈希的 HostRpc 宿主源码快照不参与格式化。

本机 Windows 环境的 `dotnet` 命令必须在管理员进程中运行。生成器或编译器加载失败时先排查构建环境，不能通过删除 RPC 合约或移除所需生成器把问题隐藏掉。

发布时递增 `plugin.json` 的版本，保留已有 SQL 迁移历史。平台审核通过和页面可用都不能替代 RPC 目录与调用测试。
