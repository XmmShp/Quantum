# Quantum .NET Plugin SDK

`Quantum.Plugin.Abstraction` 是 Quantum 唯一的插件 SDK，也是桌面宿主与本地 DLL 插件共享的稳定 ABI。

生产插件应引用发布后的 `Quantum.Plugin.Abstraction` NuGet 包。仓库内的样例使用项目引用，以便 SDK、宿主与样例一起构建和验证。

- `IQuantumPlugin` 是静态启动/停止 bootstrap，不需要注册到 DI 或创建实例；业务状态应放在普通插件服务中。
- `IQuantumPluginEnvironment` 提供已加载插件列表，并允许插件判断 manifest 中声明的弱联动是否已经激活。
- `IQuantumPluginRuntimeContext` 提供当前运行版本和只读影子目录路径。
- `IQuantumEventBus` 提供 ROS 风格的 Topic Publisher/Subscription；发布者和订阅者只需使用 JSON 结构兼容的消息模型。
- `IServiceProvider.GetService(string)` 可按 `Type.FullName`（不含程序集名和 `global::`）解析无法在编译期引用的联动服务：

```csharp
dynamic? service = services.GetService("MyNamespace.MySub.IMyInterface");
```

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

宿主从入口程序集发现 `IQuantumPlugin` 实现类型，并通过静态接口分派调用其 `StartAsync` 和 `StopAsync`；
bootstrap 不注册到 DI。两个方法收到同一个运行期 scoped provider，runtime 释放时一并释放该 scope。主插件 scope 内 `Singleton` 与 `Scoped`
通常表现相近，但 `Scoped` 可以避免服务被错误地从根 provider 解析。Web RPC 仍按调用创建独立 scope，因而需要
跨 RPC 共享的状态应使用 `Singleton`。

该扩展直接使用传入的 provider，不缓存类型或实例，也不会隐式创建作用域。解析 scoped 服务时必须传入当前
`IServiceScope.ServiceProvider`；服务及其依赖仍由对应 scope/container 释放，不应由调用方单独释放。从根 provider
解析 scoped 服务时，行为与原生 `GetService(Type)` 相同（启用 scope 校验的容器会抛出异常）。

纯 JavaScript/TypeScript 插件使用同级目录中的 `typescript` SDK。该 SDK 提供与 .NET 共用的 Topic EventBus，以及
隔离 iframe 生命周期、路由挂载、资源、导航、环境信息和 .NET 服务调用；详细说明见插件开发文档中的
Web 插件章节。
