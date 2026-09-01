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
  "dependencies": [
    { "id": "quantum.plugin.core", "minVersion": "0.1.0" }
  ],
  "integrations": [
    { "id": "quantum.plugin.theme", "minVersion": "0.1.0" }
  ],
  "permissions": [
    { "name": "files.read", "required": true }
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
- 同一目标不能同时出现在 `dependencies` 和 `integrations`，各类关系、路由和权限不能重复；未知 manifest 字段会被拒绝，避免拼写错误静默失效。

## 3. 注册服务和启动逻辑

插件装载时，宿主执行 NOF 为程序集生成的初始化器。推荐使用 `AutoInject` 注册到插件私有容器，并同时实现可逆的启动/停止逻辑：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

public interface IExampleState
{
    DateTimeOffset? StartedAt { get; }
}

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(IQuantumPlugin), typeof(IExampleState)])]
public sealed class ExampleState : IQuantumPlugin, IExampleState
{
    public DateTimeOffset? StartedAt { get; private set; }

    private CancellationTokenSource? _workerCancellation;

    public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        StartedAt = DateTimeOffset.Now;
        _workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        if (environment.IsIntegrationActive("quantum.plugin.example", "quantum.plugin.theme"))
        {
            // 注册或启动只在联动目标可用时才需要的适配逻辑。
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        _workerCancellation?.Cancel();
        _workerCancellation?.Dispose();
        _workerCancellation = null;
        return Task.CompletedTask;
    }
}
```

`IQuantumPlugin.StartAsync` 针对候选环境快照按依赖顺序调用，全部成功后宿主才发布新目录；`StopAsync` 在卸载或热切换前按逆序调用。旧版插件未实现 `StopAsync` 时会使用 SDK 的默认空实现，但这类插件只有在没有后台任务、事件订阅或外部句柄时才能安全热卸载。

`IQuantumPluginEnvironment` 提供实时的 `LoadedPlugins` 和 `IsPluginLoaded`；只有 manifest 中声明且版本兼容的关系才会被 `IsIntegrationActive` 认可。`IQuantumPluginRuntimeContext` 可用于读取当前插件版本与本代只读影子目录。插件容器会执行 NOF 生成的 `AutoInject` 初始化器，但动态插件不会加入宿主根 Application Part；依赖宿主级全局扫描的 Handler 或 Initialization Step 不属于可热卸载边界，应由稳定的宿主 Contract 显式桥接。

## 4. 提供页面

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

## 5. 提供静态资源和 Web 贡献

将资源放在插件输出目录的 `wwwroot` 下。宿主映射规则为：

```text
<plugin-root>/wwwroot/site.css
    -> _content/<plugin-id>/site.css
```

`head` 和 `postBlazor` 接受 HTML 片段。该能力等同于在宿主内执行代码，只应安装来源可信且经过审核的插件。

目录热切换时，宿主会移除旧 DOM 节点并应用新片段；已经执行的脚本副作用不会因删除 `<script>` 自动撤销。插件若注册了全局事件、定时器或 JS/.NET 引用，必须通过 `StopAsync` 调用自己的清理逻辑。

## 6. 调试

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
    └── wwwroot/
        ├── site.css
        └── main.js
```

插件程序集依赖优先从自身影子目录解析；`System.*`、`Microsoft.*`、`NOF.*` 与 Quantum 插件 ABI 始终与宿主共享。跨插件接口应放在双方共同引用的稳定 Contract/SDK 程序集中，不能依赖两个 ALC 中恰好同名的私有类型。

调试热升级流程：

1. 在 `Modules/<plugin-id>` 中替换 DLL、manifest 和 `wwwroot`；正在运行的入口 DLL 来自影子目录，因此源文件可直接覆盖。
2. 推荐同步提升 manifest `version`，便于确认切换结果。
3. 在 Quantum 首页点击“热升级”；也可以点击“重新扫描 Modules”重建整个插件快照。
4. 新快照的装载或 `StartAsync` 失败时，宿主保留/恢复旧快照并显示失败原因。

首页“卸载”仅释放运行时，不删除 `Modules` 文件。若其他插件直接或传递地强依赖目标，宿主会先列出完整级联卸载清单并要求确认；确认后，下游插件按依赖逆序先于目标停止并一同卸载。重新扫描可再次加载这些插件。为了让 collectible ALC 真正被 GC 回收，插件必须在 `StopAsync` 后消除所有后台任务、事件、委托、JS interop 和共享静态缓存对插件对象或类型的引用。

Mac Catalyst 应用运行在沙箱中，不能直接扫描普通工作区目录。macOS 调试时请把插件复制到应用数据的 `Modules` 目录；环境变量指定的目录不可访问时，宿主会回退到应用数据目录。
