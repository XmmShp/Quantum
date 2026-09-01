# Quantum 插件开发指南

Quantum 插件可以使用 .NET DLL 或 Web runtime。本篇介绍 .NET 插件；纯 JavaScript/TypeScript 插件见 [TypeScript 插件开发](web-plugin-development.md)。NOF `AutoInject` 元数据会注册到每代 .NET 插件独立的 DI 容器，使容器和程序集能在运行时一起释放。

## 1. 创建项目

使用 Razor SDK 创建类库，并引用稳定的插件 ABI 与 NOF Abstraction：

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../sdk/dotnet/src/Quantum.Plugin.Abstraction/Quantum.Plugin.Abstraction.csproj" />
    <PackageReference Include="NOF.Abstraction" />
  </ItemGroup>

  <ItemGroup>
    <Content Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="migrations\**\*.sql" CopyToOutputDirectory="PreserveNewest" />
    <Content Update="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

生产插件应引用发布后的 `Quantum.Plugin.Abstraction` 包，而不是宿主、Application 或 Infrastructure 项目。插件不得携带自己的 ABI 副本版本并尝试覆盖宿主版本。

## 2. 编写 manifest

```json
{
  "id": "quantum.plugin.example",
  "version": "0.1.0",
  "entryAssembly": "Quantum.ExamplePlugin.dll",
  "database": {
    "migrations": "./migrations"
  },
  "dependencies": [
    { "id": "quantum.plugin.core", "minVersion": "0.1.0" }
  ],
  "integrations": [
    { "id": "quantum.plugin.theme", "minVersion": "0.1.0" }
  ],
  "ui": {
    "routes": [
      {
        "path": "/plugins/example",
        "component": "Quantum.ExamplePlugin.Pages.Index",
        "title": "示例插件",
        "icon": "✦",
        "order": 100
      }
    ]
  },
  "web": {
    "head": [
      "<link rel=\"stylesheet\" href=\"_content/quantum.plugin.example/site.css\">"
    ],
    "postBlazor": [
      "<script src=\"_content/quantum.plugin.example/main.js\"></script>"
    ]
  }
}
```

插件关系分为两类：

| 字段 | 语义 | 缺失或版本不足 | 排序 |
| --- | --- | --- | --- |
| `dependencies` | 强前置 | 当前插件加载失败，依赖它的插件继续级联失败 | 前置必须先加载；硬循环失败 |
| `integrations` | 弱联动 | 当前插件继续以独立模式加载 | 兼容目标尽量先加载；软循环按稳定顺序打破 |

约束：

- `id` 使用小写字母、数字、点、下划线或连字符。
- `version` 与 `minVersion` 使用 SemVer；预发布版本参与正确的先后比较。
- 旧版 `entryAssembly` 继续受支持，等价于 `{ "runtime": { "kind": "dotnet", "entry": "..." } }`；DLL 入口只能是插件根目录下的文件名。
- .NET 路由的 `component` 必须是入口程序集内实现 `IComponent` 的完整类型名；Web 路由改用 `view`。
- `database.migrations` 对 .NET 和 Web 插件含义相同，指向插件根目录内的 SQL migration artifact。
- 同一目标不能同时出现在 `dependencies` 和 `integrations`，各类关系和路由不能重复；未知 manifest 字段会被拒绝，避免拼写错误静默失效。

## 3. 注册服务和启动逻辑

插件装载时，宿主执行 NOF 为程序集生成的初始化器。`IQuantumPlugin` 是由入口程序集静态发现的
bootstrap，不需要用 `AutoInject` 注册，也不会创建实例。业务状态、后台任务与可观察数据应放在普通服务中；
bootstrap 直接使用宿主传入的插件运行期 scope 完成启动和停止编排：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

public interface IExampleState
{
    DateTimeOffset? StartedAt { get; }
}

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(ExampleState)])]
public sealed class ExampleState
{
    public DateTimeOffset? StartedAt { get; private set; }

    internal void MarkStarted() => StartedAt = DateTimeOffset.Now;
}

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(IExampleState)])]
public sealed class ExampleStateView(ExampleState state) : IExampleState
{
    public DateTimeOffset? StartedAt => state.StartedAt;
}

public sealed class ExamplePlugin : IQuantumPlugin
{
    public static Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        services.GetRequiredService<ExampleState>().MarkStarted();
        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        if (environment.IsIntegrationActive("quantum.plugin.example", "quantum.plugin.theme"))
        {
            // 启动只在联动目标可用时才需要的适配逻辑。
        }

        return Task.CompletedTask;
    }

    public static Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
```

每代 .NET 插件拥有一个由宿主管理的 DI scope。宿主从入口程序集发现 `IQuantumPlugin` 实现类型，通过静态接口
分派调用 bootstrap；bootstrap 本身不进入 DI。`StartAsync` 与 `StopAsync` 收到同一个 scoped
`IServiceProvider`，runtime 销毁时 scope 才会释放。这与 Blazor WebAssembly
中的 scope 很接近：在主插件 scope 内 `Singleton` 与 `Scoped` 通常各只有一个实例，但使用 `Scoped` 能阻止
代码意外从根 provider 解析 scoped 服务。需要跨 Web RPC 调用共享的状态仍应注册为 `Singleton`，因为每次 RPC
会建立自己的调用 scope。

`IQuantumPlugin.StartAsync` 针对候选环境快照按依赖顺序调用，全部成功后宿主才发布新目录；`StopAsync` 在卸载或热切换前按逆序调用。两个静态方法都必须实现；即使无需清理，`StopAsync` 也应明确返回 `Task.CompletedTask`。

`IQuantumPluginEnvironment` 提供实时的 `LoadedPlugins` 和 `IsPluginLoaded`；只有 manifest 中声明且版本兼容的关系才会被 `IsIntegrationActive` 认可。`IQuantumPluginRuntimeContext` 可用于读取当前插件版本与本代只读影子目录。插件容器会执行 NOF 生成的 `AutoInject` 初始化器，但动态插件不会加入宿主根 Application Part；依赖宿主级全局扫描的 Handler 或 Initialization Step 不属于可热卸载边界，应由稳定的宿主 Contract 显式桥接。

## 4. 使用 Topic EventBus

宿主向每个 .NET 插件注入 `IQuantumEventBus`。调用 `CreatePublisher<TMessage>` 创建 Topic publisher，调用
`Subscribe` 创建 subscription：

```csharp
public sealed record DeviceStatus(string DeviceId, string State);

[AutoInject(
    ServiceLifetime.Scoped,
    RegisterTypes = [typeof(DeviceEventLifecycle)])]
internal sealed class DeviceEventLifecycle(IQuantumEventBus events)
{
    private IQuantumSubscription? _subscription;
    private IQuantumPublisher<DeviceStatus>? _publisher;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = events.Subscribe(
            QuantumTopic.Of("devices.status"),
            (@event, _) =>
            {
                var message = @event.DeserializeRequired<DeviceStatus>();
                Console.WriteLine(
                    $"{@event.Publisher.Id} published {message.State} to {@event.Topic}");
                return Task.CompletedTask;
            });
        _publisher = events.CreatePublisher<DeviceStatus>(
            QuantumTopic.Of("devices.status"));
        await _publisher.PublishAsync(
            new DeviceStatus("camera-1", "ready"),
            cancellationToken);
    }

    public async Task StopAsync()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
            _subscription = null;
        }
    }
}
```

Topic 是 NOF 的 `IValueObject<string>` 值对象，必须通过 `QuantumTopic.Of(...)` 构造；它使用点分层级，例如
`devices.camera.status`，最大长度 255，且必须匹配
`^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$`。EventBus 的 API、事件 metadata 与 Host 路由表都
持有 `QuantumTopic`，字符串只存在于系统输入边界和底层值中。当前实现是 Host 进程内的即时分发，不持久化、不重放；
`PublishAsync` 会等待当前订阅者完成。消息通过 JSON envelope 穿过 Host。

publisher 的 `TMessage` 是插件侧的序列化契约，不参与 Topic 路由，envelope 也不携带 CLR 类型标识。因此发布与
订阅插件可以声明不同但 JSON 结构兼容的 CLR DTO。订阅端只有一种 `QuantumEvent`，它始终保留原始 `JsonElement`
Payload，并提供 `Deserialize<T>()`、`DeserializeRequired<T>()`、`Deserialize(Type)` 和 `TryDeserialize<T>()`
按需转换：

```csharp
var subscription = events.Subscribe(
    QuantumTopic.Of("devices.status"),
    (@event, _) =>
    {
        var state = @event.Payload.GetProperty("state").GetString();
        var message = @event.DeserializeRequired<DeviceStatus>();
        return Task.CompletedTask;
    });
```

不要在消息中依赖对象引用、私有类型身份或无法序列化的状态。

插件停止时 Host 会暂停该运行代的发布和回调，容器释放时也会兜底移除全部订阅。生命周期仍应在 `StopAsync`
主动释放 `IQuantumSubscription`，以支持热切换失败后的旧运行代回滚启动。一个订阅失败不会阻止同 Topic 的其他订阅，
所有失败会在发布端合并为 `AggregateException`。

## 5. 提供页面

页面不需要 `@page`，路由以 manifest 为准：

```razor
@namespace Quantum.ExamplePlugin.Pages
<h1>Example</h1>
<p>Started at @State.StartedAt</p>

@code {
    private readonly IExampleState State;

    // 插件私有服务使用构造注入，由 Quantum 的插件组件激活器从私有容器解析。
    public Index(IExampleState state)
    {
        State = state;
    }
}
```

宿主校验类型后使用 `DynamicComponent` 渲染，并根据 manifest 的 `title`、`icon` 和 `order` 生成菜单。插件私有服务必须使用构造注入；Blazor 的 `@inject` 属性仍由宿主渲染器处理，只适合注入宿主公开的稳定服务。为避免 Router 缓存旧程序集，动态插件页面只使用 manifest 路由，不支持插件程序集内的 `@page` 自动发现。

## 6. 提供静态资源和 Web 贡献

将资源放在插件输出目录的 `wwwroot` 下。宿主映射规则为：

```text
<plugin-root>/wwwroot/site.css
    -> _content/<plugin-id>/site.css
```

`head` 和 `postBlazor` 接受 HTML 片段。该能力等同于在宿主内执行代码，只应安装来源可信且经过审核的插件。

动态插件不会在宿主编译期作为 Razor 项目引用，因此 CSS isolation 生成的 project bundle 需要由插件项目复制到输出目录的
`wwwroot`，再通过 manifest 的 `web.head` 引用。`samples/Quantum.ExampleCalendarPlugin` 展示了完整做法：
`Calendar.razor.css` 构建为 `Quantum.ExampleCalendarPlugin.bundle.scp.css`，组件的 scope attribute 与 bundle
选择器仍由 Razor SDK 自动生成。

同一个日历示例也演示了宿主托管的共享持久化。发布包以 manifest 声明唯一、语言无关的 migration 能力：

```json
{
  "database": {
    "migrations": "./migrations"
  }
}
```

artifact 是插件根目录下的累积 SQL 历史，与运行时和开发 ORM 无关：

```text
migrations/
├── 001_init.sql
├── 002_add_index.sql
└── 003_add_status.sql
```

当前 Host 数据库是 SQLite，因此发布 SQL 必须使用 SQLite 方言。开发阶段可以自由使用 EF Core、Prisma、
Drizzle 或其他工具管理模型，但发布前必须把最终升级路径导出为这些 SQL 文件，随插件包一起交付。Host 不加载
ORM migration assembly，也不执行 Prisma/Drizzle 的运行时 migration metadata。

artifact 约束如下：

- 目录相对于插件根目录；`./migrations` 与 `migrations` 等价，不能使用绝对路径、反斜杠或 `..`。
- 目录不允许嵌套，至少包含一个非空 UTF-8 文件；文件名使用 `<数字>_<描述>.sql`，数字序号必须唯一并按数值排序。
- 每个新版本必须携带从第一条开始的完整历史，只能在末尾追加文件。已经发布的文件不得改名、删除或修改内容。
- Host 在插件 `StartAsync` 之前执行待应用文件，并把该插件的整批待应用 SQL 和历史记录放在同一个事务中；SQL 文件不要自行执行 `BEGIN`、`COMMIT` 或 `ROLLBACK`。
- Host 在 `__quantum_plugin_migrations` 中按插件记录文件名、SHA-256、发布版本和应用时间。checksum 漂移、历史缺失或中间插入会阻止新 runtime 启动。

migration 是 forward-only 的持久化提交。新 runtime 后续启动失败时 Host 可以恢复旧代码运行，但不会执行 `Down`
或撤销已经成功提交的 schema；升级 SQL 应采用 expand/migrate/contract，先添加兼容结构，等不再需要旧版本后再删除旧结构。

`Application/CalendarItemApplicationService` 只依赖 `NOF.Domain.IRepository<CalendarItem>` 与
`NOF.Application.IDbContext`；插件自身最多引用 `NOF.Infrastructure`，不引用 EF Core、SQLite provider 或宿主持久化项目。
运行期仍可通过纯 NOF 抽象贡献 EF 模型：

```csharp
internal sealed class CalendarDbContextModelCreatingContributor
    : IDbContextModelCreatingContributor
{
    public void Configure(IDbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalendarItem>(entity =>
        {
            entity.ToTable("CalendarPluginItems");
            entity.IsHostOnly();
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(120).IsRequired();
        });
    }
}
```

插件初始化器将 contributor 注册为 `IDbContextModelCreatingContributor`。Quantum 宿主检测到该注册后，向该插件的
私有 DI 容器加入 NOF EF adapter；contributor 定义运行期对象映射，发布 SQL artifact 定义版本间 schema 演进。
页面通过构造注入的应用服务读写事项，`StartAsync` 只负责首次示例数据。所有插件连接同一个宿主数据库：

```text
<ApplicationData>/quantum.db
```

每个插件仍使用自己的 DbContext/DI 生命周期和只包含本插件实体的 EF 模型，以免宿主根容器持有可卸载程序集；它们共享的是
物理 SQLite 文件。表名位于全局命名空间，插件应使用稳定且包含插件标识的表名，避免与其他插件冲突。宿主关闭 SQLite
连接池和 EF provider cache，确保 DbContext、contributor 与 collectible ALC 能一起释放。

不要把数据库写入 `IQuantumPluginRuntimeContext.RootPath`；该路径属于当前运行代的只读影子副本，热升级或卸载后会被清理。

目录热切换时，宿主会移除旧 DOM 节点并应用新片段；已经执行的脚本副作用不会因删除 `<script>` 自动撤销。插件若注册了全局事件、定时器或 JS/.NET 引用，必须通过 `StopAsync` 调用自己的清理逻辑。

## 7. 调试

先构建插件，再让宿主扫描包含插件子目录且宿主有权访问的根路径：

```bash
dotnet build MyPlugin.csproj

QUANTUM_MODULES_PATH=/absolute/path/to/Modules \
dotnet build quantum/src/Quantum/Quantum.csproj -t:Run -f net10.0-maccatalyst
```

示例目录：

```text
Modules/
└── quantum.plugin.example/
    ├── plugin.json
    ├── Quantum.ExamplePlugin.dll
    ├── ThirdParty.Dependency.dll
    ├── migrations/
    │   └── 001_init.sql
    └── wwwroot/
        ├── site.css
        └── main.js
```

插件程序集依赖优先从自身影子目录解析；`System.*`、`Microsoft.*`、`NOF.*` 与 Quantum 插件 ABI 始终与宿主共享。跨插件接口应放在双方共同引用的稳定 Contract/SDK 程序集中，不能依赖两个 ALC 中恰好同名的私有类型。

调试热升级流程：

1. 在 `Modules/<plugin-id>` 中替换 DLL、manifest 和 `wwwroot`；正在运行的入口 DLL 来自影子目录，因此源文件可直接覆盖。
2. 推荐同步提升 manifest `version`，便于确认切换结果。
3. 在 Quantum 首页点击“热升级”；也可以点击“重新扫描 Modules”重建整个插件快照。
4. 新快照的装载、SQL migration 或 `StartAsync` 失败时，宿主保留/恢复旧 runtime 并显示失败原因；已经成功提交的 forward-only migration 不回滚。

首页“卸载”仅释放运行时，不删除 `Modules` 文件。若其他插件直接或传递地强依赖目标，宿主会先列出完整级联卸载清单并要求确认；确认后，下游插件按依赖逆序先于目标停止并一同卸载。重新扫描可再次加载这些插件。为了让 collectible ALC 真正被 GC 回收，插件必须在 `StopAsync` 后消除所有后台任务、事件、委托、JS interop 和共享静态缓存对插件对象或类型的引用。

Mac Catalyst 应用运行在沙箱中，不能直接扫描普通工作区目录。macOS 调试时请把插件复制到应用数据的 `Modules` 目录；环境变量指定的目录不可访问时，宿主会回退到应用数据目录。
