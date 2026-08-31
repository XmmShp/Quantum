# Quantum 插件开发指南

Quantum 插件由一个 `net10.0` DLL、一份 `plugin.json` 和可选的 `wwwroot` 组成。服务注册由 NOF Application Part 和 `AutoInject` 完成。

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
    <ProjectReference Include="../../sdk/Quantum.Plugin.Abstraction/Quantum.Plugin.Abstraction.csproj" />
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

约束：

- `id` 使用小写字母、数字、点、下划线或连字符。
- `version` 与 `minVersion` 使用 SemVer；预发布版本参与正确的先后比较。
- `entryAssembly` 只能是当前插件目录下的 DLL 文件名，不能包含路径。
- `component` 必须是入口程序集内实现 `IComponent` 的完整类型名。
- 路由、依赖和权限不能重复；未知 manifest 字段会被拒绝，避免拼写错误静默失效。

## 3. 注册服务和启动逻辑

插件程序集在宿主 `BuildAsync` 前通过 `AddApplicationPart` 加入 NOF。推荐直接使用 `AutoInject`：

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

    public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        StartedAt = DateTimeOffset.Now;
        return Task.CompletedTask;
    }
}
```

`IQuantumPlugin.StartAsync` 由宿主的 NOF Initialization Step 调用。单个插件的启动异常会被记录，但不会阻止桌面宿主启动。插件也可以直接提供 NOF 支持的 Handler 和 Initialization Step。

## 4. 提供页面

页面不需要 `@page`，路由以 manifest 为准：

```razor
@namespace Quantum.ExamplePlugin.Pages
@inject IExampleState State

<h1>Example</h1>
<p>Started at @State.StartedAt</p>
```

宿主校验类型后使用 `DynamicComponent` 渲染，并根据 manifest 的 `title`、`icon` 和 `order` 生成菜单。插件仍可添加普通 Blazor `@page`，宿主 Router 会把已加载插件程序集作为 Additional Assemblies，但推荐只保留一种路由来源。

## 5. 提供静态资源和 Web 贡献

将资源放在插件输出目录的 `wwwroot` 下。宿主映射规则为：

```text
<plugin-root>/wwwroot/site.css
    -> _content/<plugin-id>/site.css
```

`head` 和 `postBlazor` 接受 HTML 片段。该能力等同于在宿主内执行代码，只应安装来源可信且经过审核的插件。

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

插件依赖优先从自身目录解析；`System.*`、`Microsoft.*`、`NOF.*` 与 Quantum 插件 ABI 始终与宿主共享。插件更新或卸载后需要重启应用。

Mac Catalyst 应用运行在沙箱中，不能直接扫描普通工作区目录。macOS 调试时请把插件复制到应用数据的 `Modules` 目录；环境变量指定的目录不可访问时，宿主会回退到应用数据目录。
