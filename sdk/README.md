# Quantum .NET Plugin SDK

`Quantum.Plugin.Abstraction` 是 Quantum 唯一的插件 SDK，也是桌面宿主与本地 DLL 插件共享的稳定 ABI。

生产插件应引用发布后的 `Quantum.Plugin.Abstraction` NuGet 包。仓库内的样例使用项目引用，以便 SDK、宿主与样例一起构建和验证。

- `IQuantumPlugin` 是静态 bootstrap：可通过默认空实现的 `ConfigureServices` 配置本代私有 DI，并通过
  `StartAsync`/`StopAsync` 编排生命周期；bootstrap 本身不注册到 DI，也不会创建实例。
- `IQuantumPluginEnvironment` 提供已加载插件列表和查询；联动逻辑可以直接按目标插件是否可用来决定。
- `IQuantumPluginRuntimeContext` 提供当前运行版本和只读影子目录路径。
- `IQuantumEventBus` 提供 ROS 风格的 Topic Publisher/Subscription；发布者和订阅者只需使用 JSON 结构兼容的消息模型。
- `IRpcInvoker` 通过稳定 RPC 名称调用其他插件的 NOF `RpcServer` Handler；调用方不会取得目标插件的 provider、服务实例或 CLR 类型。

## 插件 RPC

服务端使用 NOF Contract，并用 Quantum transport 与调用名称标注契约：

```csharp
[TransportOverQuantum]
[RpcInvocationName("notes")]
public interface INotesService : IRpcService
{
    [RpcInvocationName("find")]
    [RpcInvocationAlias("search.notes")]
    Result<Note[]> Find(FindNotesRequest request);
}

public partial class NotesService : RpcServer<INotesService>;

public sealed class FindNotes(NoteStore store) : NotesService.Find
{
    public override Task<Result<Note[]>> HandleAsync(
        FindNotesRequest request,
        Context context,
        CancellationToken cancellationToken)
        => store.FindAsync(request, cancellationToken);
}
```

假设实现插件 id 为 `quantum.plugin.notes`，以上方法可以通过完整名称
`quantum.plugin.notes.notes.find`、短名称 `notes.find` 或 Alias `search.notes` 调用。所有名称忽略大小写。
短名称或 Alias 有多个实现时，Host 记录 Warning，并选择实现方法所在 `pluginId` 按 ordinal 字典序最小的一项。

调用方从自己的 DI 获取 `IRpcInvoker`：

```csharp
var result = await services.GetRequiredService<IRpcInvoker>().InvokeAsync<Note[]>(
    "quantum.plugin.notes.notes.find",
    new { text = "quantum" },
    Context.Empty,
    cancellationToken);
```

找不到名称时返回失败的 `Result`（`rpc_not_found`），不会抛出“服务未注册”异常。Payload、Context 与 Result
始终经过 JSON 边界；每次调用在目标插件中创建独立 scope，结果序列化完成后释放。manifest 的 `dependencies` 与
`integrations` 不构成 RPC 权限，只描述加载约束或软排序关系。

## 插件标识与版本值对象

`QuantumPluginInfo.Id` 和 `QuantumPluginInfo.Version` 分别是 `PluginId` 与 `SemanticVersion`，不再是未校验的
`string`。两者均直接实现 NOF 的 `IValueObject<string>`；`Of(...)`、底层值转换、相等性、`ToString()` 和 JSON
converter 由 NOF source generator 提供。`PluginId` 会去除首尾空白并转为小写，限制为 1–128 个 ASCII 字符，只允许字母、数字、点、下划线和
连字符，首尾必须是字母或数字；`disabled` 是宿主保留值。动态查询也要显式构造值对象：

```csharp
var themeId = PluginId.Of("quantum.plugin.theme");
if (environment.IsPluginLoaded(themeId))
{
    // 目标插件当前可用。
}
```

`SemanticVersion.Of(...)` 严格接受 SemVer 2.0.0（必须包含 `major.minor.patch`），提供 `Major`、`Minor`、
`Patch`、`PreReleaseIdentifiers`、`BuildMetadataIdentifiers` 和 `IsPreRelease`。三个数字字段使用 `BigInteger`，
不会额外引入 `Int32` 上限；`CompareTo` 以及 `<`、`<=`、`>`、`>=` 按 SemVer 优先级比较，构建元数据不参与
优先级：

```csharp
var current = SemanticVersion.Of("2.1.0-rc.2+linux.arm64");
var minimum = SemanticVersion.Of("2.1.0-beta.1");

if (current >= minimum)
{
    Console.WriteLine(string.Join('.', current.PreReleaseIdentifiers));
}
```

`VersionRange.Of(...)` 校验 manifest 中 `dependencies`/`integrations` 使用的范围，并通过 `Contains(...)` 判断版本：

```csharp
var range = VersionRange.Of("{1.2.3} | [1.3.0,1.4.0) | (1.4.0,1.5.0)");
var compatible = range.Contains(SemanticVersion.Of("1.2.3+linux-x64")); // true
```

它支持有界或无界区间、有限版本集合以及 `|` 并集；`(,)` 和 `*` 均表示全部版本。预发布版本直接按 SemVer
precedence 参与范围比较，构建元数据完全不参与比较。

三个值对象通过 NOF 生成的 JSON converter 序列化为字符串，所以 EventBus envelope 和 Web Host 协议的字段形状保持不变。

数据库 schema 演进不属于 .NET ABI。插件可以在开发期使用 EF/NOF 模型，但发布包统一通过
`plugin.json` 的 `database.migrations` 携带 SQLite SQL artifact；Host 在 `StartAsync` 前应用它。具体规则见
[插件开发指南](../docs/plugin-development.md#6-提供静态资源和-web-贡献)。

## Topic EventBus

从插件 DI 获取 `IQuantumEventBus`，为一个 Topic 创建强类型 publisher，并持有 subscription 直到停止：

```csharp
public sealed record DeviceStatus(string DeviceId, string State);

internal sealed class DeviceEvents(IQuantumEventBus events)
{
    private IQuantumSubscription? _statusSubscription;
    private IQuantumPublisher<DeviceStatus>? _statusPublisher;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _statusSubscription = events.Subscribe(
            QuantumTopic.Of("devices.status"),
            (@event, _) =>
            {
                var message = @event.DeserializeRequired<DeviceStatus>();
                Console.WriteLine($"{@event.Publisher.Id}: {message.State}");
                return Task.CompletedTask;
            });
        _statusPublisher = events.CreatePublisher<DeviceStatus>(
            QuantumTopic.Of("devices.status"));
        await _statusPublisher.PublishAsync(
            new DeviceStatus("camera-1", "ready"),
            cancellationToken);
    }

    public async Task StopAsync()
    {
        if (_statusSubscription is not null)
        {
            await _statusSubscription.DisposeAsync();
            _statusSubscription = null;
        }
    }
}
```

Topic 是 NOF 的 `IValueObject<string>` 值对象，通过 `QuantumTopic.Of(...)` 创建并在边界完成校验。它使用点分层级，
例如 `devices.camera.status`，最大长度 255，并且必须匹配
`^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$`。
EventBus 是 Host 内的即时分发，不持久化、不重放：`PublishAsync` 等待发布时已经存在的订阅回调完成，并在回调失败时
报告 `AggregateException`。消息先写入 JSON envelope，再由 NOF `InMemoryEventHandler` 转发。

publisher 的 `TMessage` 只定义插件一侧的 CLR 序列化契约，不是路由键，也不会以 CLR 类型标识写入 envelope。因此
不同插件 ALC 可以用各自的 CLR 类型消费同一 Topic，只要 JSON 字段兼容。订阅统一接收 `QuantumEvent`，可直接读取
`Payload`，也可按需调用 `Deserialize<T>()`、`DeserializeRequired<T>()`、`Deserialize(Type)` 或
`TryDeserialize<T>()`：

```csharp
await using var raw = events.Subscribe(
    QuantumTopic.Of("devices.status"),
    (@event, _) =>
    {
        var state = @event.Payload.GetProperty("state").GetString();
        var message = @event.DeserializeRequired<DeviceStatus>();
        return Task.CompletedTask;
    });
```

对象引用和私有 CLR 类型身份不会跨插件传递。

Host 会在插件停止时暂停该运行代的收发，并在容器释放时兜底移除订阅。插件仍应在 `StopAsync` 主动释放
`IQuantumSubscription`，这样热切换失败并回滚旧运行代时，`StartAsync` 可以无重复订阅地重新建立监听。

宿主从入口程序集发现 `IQuantumPlugin` 实现类型。构建容器前，宿主先执行程序集中的所有 NOF
`IAssemblyInitializer`，再通过静态接口分派调用每个 bootstrap 的 `ConfigureServices`；两条服务注册链路都会执行，
后者可以追加或覆盖生成注册。`ConfigureServices` 有默认空实现。bootstrap 不注册到 DI，也不会创建实例。
容器构建后，宿主通过静态接口分派调用 `StartAsync` 和 `StopAsync`，两个方法收到同一个运行期 scoped provider，
runtime 释放时一并释放该 scope。主插件 scope 内 `Singleton` 与 `Scoped`
通常表现相近，但 `Scoped` 可以避免服务被错误地从根 provider 解析。插件 RPC 按调用创建独立 scope，因而需要
跨 RPC 共享的状态应使用 `Singleton`。

RPC 调用方只持有 Host 提供的 `IRpcInvoker`，不会缓存目标 provider、Handler 或服务实例。Host 在运行代停止时先撤销
路由并等待在途调用结束，再释放目标 scope/container 和 collectible ALC。

纯 JavaScript/TypeScript 插件使用同级目录中的 `typescript` SDK。该 SDK 提供与 .NET 共用的 Topic EventBus，以及
隔离 iframe 生命周期、路由挂载、资源、导航、环境信息和同一套名称路由 RPC；详细说明见插件开发文档中的
Web 插件章节。
