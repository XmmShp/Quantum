# 官方插件开发：必须暴露 NOF RPC

## 为什么只有页面还不够

Quantum 的插件页面与 RPC 能力是两条独立的链路。Razor 页面能正常显示、能通过 DI 调用应用服务、能读写数据库，并不代表 Quantum 能发现或调用这些业务接口。

**凡是需要让 Quantum、其他插件或 AI 工具读取和调用的业务能力，都必须通过 NOF RPC 暴露。** 仅添加 `public` 方法、注册应用服务、在 manifest 中声明 UI 路由，均不会把方法加入 RPC 目录。

记账插件 `0.1.0` 曾经只提供页面和 `LedgerService`，因此手动记账可用，但宿主 RPC 目录中没有记账接口。`0.1.1` 补齐了 NOF 合约、生成服务、处理器注册和宿主调用测试。本说明用于避免后续插件重复出现这个问题。

## 从合约到宿主调用

以 [Ledger/Rpc/LedgerRpcService.cs](../Ledger/Rpc/LedgerRpcService.cs) 为完整示例：

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
4. 从生成的 `LedgerRpcService.ListEntries` 等处理器基类派生实现。处理器调用同一套应用服务、传递 `CancellationToken`，将参数错误和未找到记录转换为明确的业务失败 Result，不吞掉取消异常。
5. 在 `IQuantumPlugin.ConfigureServices` 或 `IAssemblyInitializer.Initialize` 注册**生成的处理器基类 → 具体处理器**。例如：

   ```csharp
   services.AddTransient<LedgerRpcService.ListEntries, ListLedgerEntries>();
   ```

   仅注册 `LedgerService` 不够。每一个公开 RPC 方法都要有可解析的处理器。
6. 自建 DI scope 后立即调用 `scope.ServiceProvider.ResolveDaemonServices()`，遵循 NOF 生命周期要求。数据库提交应保持原子性，失败操作的跟踪状态不能泄漏到下一次保存。
7. 给服务、方法和请求属性写清楚 `DescriptionAttribute`。宿主会把这些说明与输入输出 JSON Schema 一起导出，供 Quantum 和 AI 工具理解用途、单位、枚举及限制。明确注明修改和删除等副作用。

宿主扫描插件入口程序集中的 NOF RPC 服务，读取上述元数据，只暴露带 `[TransportOverQuantum]` 的合约。以记账插件为例：

| 名称 | 用途 |
| --- | --- |
| `quantum.rpc.catalog` | 获取当前已激活的宿主及插件 RPC 目录、描述和 Schema |
| `quantum.plugin.ledger.ledger.list` | 带插件 ID 的稳定、完整调用名，集成代码优先使用 |
| `ledger.list` | 短调用名；多个插件重名时，宿主按插件 ID 的 ordinal 字典序选择 |

公开 RPC 不要求调用方额外声明 `dependencies` 或 `integrations`；这两项控制加载关系，不负责暴露 API。仍需先安装并激活提供方插件。插件停止后不应继续出现在可用能力目录中。

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

测试应包括查询、新增、修改、删除、无效请求、缺失记录和取消。写入后应通过另一次 RPC 查询验证持久化结果，而不只是直接调用处理器或应用服务。对于导入，要验证重复导入和冲突时整次拒绝。

[记账 RPC 集成测试](../tests/Quantum.LedgerPlugin.Tests/LedgerRpcTests.cs) 使用真实 SQLite 和从 Quantum 宿主固定版本取出的 RPC 发现、目录、序列化及调度实现，实际扫描记账插件 DLL 并执行调用。宿主测试快照的来源和哈希记录在 [HostRpc/provenance.json](../tests/Quantum.LedgerPlugin.Tests/HostRpc/provenance.json)，升级宿主 ABI 或发现规则时应同步更新并重新验证。

运行：

```sh
dotnet test tests/Quantum.LedgerPlugin.Tests/Quantum.LedgerPlugin.Tests.csproj -c Release
```

本机 Windows 环境的 `dotnet` 命令必须在管理员进程中运行。生成器或编译器加载失败时先排查构建环境，不能通过删除 RPC 合约或移除所需生成器把问题隐藏掉。

发布时递增 `plugin.json` 的版本，保留已有 SQL 迁移历史。平台审核通过和页面可用都不能替代 RPC 目录与调用测试。
