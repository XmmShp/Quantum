# Quantum

Quantum 是基于 .NET 10、NOF 与 .NET MAUI Blazor Hybrid 的本地优先插件平台。桌面宿主负责窗口、WebView、导航和安全边界；插件可以使用 .NET DLL，也可以使用运行在隔离 iframe 中的 JavaScript/TypeScript。

## 核心能力

- `NOFMauiAppBuilder` 驱动 MAUI 应用和 NOF 初始化管线。
- 插件按 `plugin.json` 发现；`dependencies` 提供强前置约束，`integrations` 只提供声明性软排序提示，不限制联动逻辑或跨插件调用。
- 每个 .NET 插件使用独立、可回收的 `AssemblyLoadContext` 和 DI 容器；入口 DLL 从影子目录以流方式加载，源文件可随时替换。
- Web 插件使用独立的 opaque-origin iframe；入口以单文件 ESM 加载，销毁 iframe 即可释放 DOM、定时器和模块运行环境。
- 静态 `IQuantumPlugin` bootstrap 无需注册到 DI 或创建实例，通过插件运行期 scope 驱动可逆 .NET 生命周期；卸载与热升级无需重启宿主，.NET 启动失败会自动回滚旧快照。
- NOF `AutoInject` 生成的注册元数据在插件私有容器内执行，不会让宿主根 DI 持有插件类型。
- .NET 与 TypeScript 插件共享 Topic EventBus；Host 使用 NOF 内存事件和 JSON envelope 在 ALC、iframe 运行代之间转发，并等待异步订阅者完成。
- manifest 页面通过 `DynamicComponent`（.NET）或 iframe view（Web）注入路由和菜单。
- `.NET` 与 Web 插件通过同一个 `database.migrations` manifest 能力发布追加式 SQLite SQL migrations；Host 统一排序、事务执行并校验历史 checksum。
- 插件 `wwwroot` 通过自定义 `IFileProvider` 映射为 `_content/{pluginId}/...`。
- 整个应用窗口支持拖拽插件整合包 ZIP；单插件包使用相同流程。宿主会按 SemVer 合并版本、预检整份依赖/运行时快照，用户确认后再事务化写入 `Modules`。
- `head` 与 `postBlazor` Web 贡献在 Blazor 启动后注入；脚本节点会被重新创建以确保执行。

## 目录结构

```text
quantum/
├── src/
│   └── Quantum/                 桌面宿主、插件模型、依赖规划与运行时实现
│       ├── Plugins/             manifest、ALC、EventBus、目录与生命周期
│       ├── Components/          Blazor Hybrid UI
│       └── Platforms/           macOS 与 Windows 启动入口
└── tests/
    └── Quantum.Tests/           插件模型与运行时测试
quantum-platform/
├── src/                         NOF 分层的平台身份、插件市场与 JSON-RPC Host
└── tests/                       平台领域与安全存储测试
sdk/
├── dotnet/
│   ├── src/                     插件与宿主共享的 .NET ABI/SDK
│   └── test/                    .NET SDK 独立测试
└── typescript/                  Web 插件生命周期与互操作类型
samples/
├── Quantum.ExamplePlugin/          .NET、Blazor、DI 与静态资源示例
├── Quantum.ExampleDependentPlugin/ 声明并验证强前置依赖的 .NET 示例
├── Quantum.ExampleCalendarPlugin/  bundled CSS + 宿主共享 SQLite 的 NOF CRUD 示例
└── Quantum.ExampleWebPlugin/       纯 TypeScript iframe 插件示例
docs/                            架构与插件开发文档
```

## 本地开发

要求：

- .NET SDK 10.0.203 或兼容的 10.0 patch
- 对应平台的 .NET MAUI workload
- macOS 上使用 Mac Catalyst；Windows 上使用 WinUI

```bash
dotnet restore Quantum.slnx
dotnet build Quantum.slnx
dotnet test quantum/tests/Quantum.Tests/Quantum.Tests.csproj
dotnet test quantum-platform/tests/Quantum.Platform.Tests/Quantum.Platform.Tests.csproj
dotnet test sdk/dotnet/test/Quantum.Plugin.Abstraction.Tests/Quantum.Plugin.Abstraction.Tests.csproj
npm ci --prefix sdk/typescript && npm test --prefix sdk/typescript
npm ci --prefix samples/Quantum.ExampleWebPlugin && npm test --prefix samples/Quantum.ExampleWebPlugin
```

macOS 启动：

```bash
dotnet build quantum/src/Quantum/Quantum.csproj \
  -t:Run \
  -f net10.0-maccatalyst
```

Windows 启动（PowerShell）：

```powershell
dotnet run --project quantum/src/Quantum/Quantum.csproj `
  --framework net10.0-windows10.0.19041.0
```

Windows Debug 构建使用未打包、自包含的 Windows App SDK 模式，可直接从工作区启动，无需预先安装对应版本的 Windows App Runtime。

Debug 构建会把示例插件暂存到宿主输出目录，便于本地调试与独立加载测试。也可以显式指定宿主有权访问的插件根目录：

```bash
QUANTUM_MODULES_PATH=/absolute/path/to/Modules \
dotnet build quantum/src/Quantum/Quantum.csproj -t:Run -f net10.0-maccatalyst
```

目录中的每个直接子目录代表一个插件，至少包含 `plugin.json`，以及 manifest 指定的 .NET 入口 DLL 或
`wwwroot` 下的 Web 入口 ESM bundle；声明数据库能力时还必须包含完整的 SQL migration artifact。`disabled`
是宿主保留的插件 id 和目录名，用于保存已禁用插件。

运行时可在设置页对插件执行“热升级”“禁用”“重新启用”或“卸载”，也可重新扫描整个 `Modules`。禁用前界面会列出所有直接和传递强依赖插件；确认后按依赖顺序停用，并把整组物理目录移动到 `Modules/disabled`。重新启用会先恢复目录并重新执行完整依赖与兼容性检查，失败时移回禁用区。卸载是不可恢复操作：运行时释放 .NET 生命周期、私有 DI 容器和 ALC（或销毁 Web iframe）后，插件及级联强依赖的物理目录会被直接删除。热升级先从源目录创建未激活的候选影子副本，切换失败时自动回滚旧版本。

Mac Catalyst 受应用沙箱限制，不能直接读取任意工作区路径；macOS 插件应安装到应用数据目录。不可访问的 `QUANTUM_MODULES_PATH` 会安全回退到该目录，并在设置页显示一条加载异常。

Catalyst Release 默认的 AOT-only 模式不能加载外部 IL，因此桌面宿主在该目标上显式启用 Mono interpreter 并关闭托管程序集裁剪。若发行渠道是 Mac App Store，还需要单独评估性能、包体与动态插件审核政策；Windows 的 CoreCLR 插件模式不受此约束。

宿主默认将 Debug 及以上级别的结构化日志写入应用数据目录的
`Logs/quantum-YYYYMMDD-HHmmss-PID-SEGMENT.log`。文件名中的时间为应用启动时间，PID 用于
避免同一秒启动的不同进程发生冲突；每段最大 20 MiB，并从 `-0.log` 开始依次递增分段编号。
同一应用生命周期的所有分段共享同一个启动时间与 PID，不同进程使用不同的文件前缀。插件加载、
生命周期、Web RPC 与 iframe Host 的诊断信息均使用同一日志管线。

需要在终端同步查看日志时，传入 `--console`。使用 `dotnet run` 时，参数需要放在第二个
`--` 之后。Windows GUI 宿主会在启动时附着到当前终端；如果没有父终端，则创建一个
控制台窗口：

```powershell
dotnet run --project quantum/src/Quantum/Quantum.csproj `
  --framework net10.0-windows10.0.19041.0 -- --console
```

## 插件开发

完整流程见 [插件开发指南](docs/plugin-development.md)，架构边界见 [架构说明](docs/architecture.md)。可直接从 [Quantum.ExamplePlugin](samples/Quantum.ExamplePlugin) 复制起步；需要声明强前置依赖时参考 [Quantum.ExampleDependentPlugin](samples/Quantum.ExampleDependentPlugin)。

## 平台服务

[Quantum Platform](quantum-platform/README.md) 是独立部署的统一后端平台，使用 .NET 10 + NOF 的 Domain/Contract/Application/Host 分层。平台当前提供用户身份与权限，以及插件发布、审核、下载、兼容性和审计能力；后续平台业务继续在这一边界内扩展。Contract 统一通过 `/rpc` 的 JSON-RPC 2.0 暴露，PostgreSQL、JWT、ZIP 安全校验和 Docker 部署说明见子项目文档。

## 路线图

下一阶段将先完成可信的[远程插件下发](ROADMAP.md#第一阶段远程插件下发)，再基于 Web 插件运行时建设 [Android 与 iOS 移动端](ROADMAP.md#第二阶段移动端)。详细里程碑和验收标准见 [ROADMAP](ROADMAP.md)。

## 许可证

MIT，详见 [LICENSE](LICENSE)。
