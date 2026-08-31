# Quantum 2.0

Quantum 是基于 .NET 10、NOF 与 .NET MAUI Blazor Hybrid 的本地优先插件平台。桌面宿主负责窗口、WebView、导航和安全边界；插件以 DLL 形式提供服务、页面、静态资源以及受信任的 HTML/JS/CSS 扩展。

## 当前基线

- `NOFMauiAppBuilder` 驱动 MAUI 应用和 NOF 初始化管线。
- 插件按 `plugin.json` 发现，并在加载前完成语义化版本校验和依赖拓扑排序。
- 每个插件使用独立、可回收的 `AssemblyLoadContext`；V1 只加载不卸载，更新后重启生效。
- 插件程序集作为 NOF Application Part 加入宿主，可使用 NOF `AutoInject`、Handler 和 Initialization Step。
- manifest 页面通过 `DynamicComponent` 注入路由和菜单。
- 插件 `wwwroot` 通过自定义 `IFileProvider` 映射为 `_content/{pluginId}/...`。
- `head` 与 `postBlazor` Web 贡献在 Blazor 启动后注入；脚本节点会被重新创建以确保执行。

本次基线覆盖 M1、M2 和 M3 的核心纵向链路。M4 的安装/更新事务、签名验证和插件管理页尚未实现。

## 目录结构

```text
quantum/
├── src/
│   ├── Quantum.Domain/          插件模型、版本、依赖、权限和安装状态
│   ├── Quantum.Contract/        对外契约
│   ├── Quantum.Application/     依赖规划、运行目录与路由注册
│   ├── Quantum.Infrastructure/  manifest、ALC、文件系统与静态资源实现
│   └── Quantum.Host/            NOF MAUI Blazor Hybrid 桌面宿主
└── tests/
    └── Quantum.Tests/           领域和加载基础设施测试
quantum-extension-market/
├── src/                         NOF 分层的插件市场与 JSON-RPC Host
└── tests/                       市场领域与安全存储测试
sdk/
└── Quantum.Plugin.Abstraction/  插件与宿主共享的 .NET ABI/SDK
samples/
└── Quantum.ExamplePlugin/       页面、DI、CSS 与 JS 的完整示例插件
docs/                            架构与插件开发文档
```

Quantum 1.0 的 Electron `Runtime`、旧 `Quantum.Sdk`、`Quantum.BundleTool` 和 `Samples/Template*` 均已移除。仓库按产品、SDK、样例和文档边界组织。

## 本地开发

要求：

- .NET SDK 10.0.203 或兼容的 10.0 patch
- 对应平台的 .NET MAUI workload
- macOS 上使用 Mac Catalyst；Windows 上使用 WinUI

```bash
dotnet restore Quantum.slnx
dotnet build Quantum.slnx
dotnet test quantum/tests/Quantum.Tests/Quantum.Tests.csproj
dotnet test quantum-extension-market/tests/Quantum.ExtensionMarket.Tests/Quantum.ExtensionMarket.Tests.csproj
```

macOS 启动：

```bash
dotnet build quantum/src/Quantum.Host/Quantum.Host.csproj \
  -t:Run \
  -f net10.0-maccatalyst
```

Debug 构建会把示例插件暂存到 Host 输出目录，便于 Windows 调试与独立加载测试。也可以显式指定宿主有权访问的插件根目录：

```bash
QUANTUM_MODULES_PATH=/absolute/path/to/Modules \
dotnet build quantum/src/Quantum.Host/Quantum.Host.csproj -t:Run -f net10.0-maccatalyst
```

目录中的每个直接子目录代表一个插件，至少包含 `plugin.json` 与入口 DLL。

Mac Catalyst 受应用沙箱限制，不能直接读取任意工作区路径；macOS 插件应安装到应用数据目录。不可访问的 `QUANTUM_MODULES_PATH` 会安全回退到该目录，并在首页显示一条加载异常。

Catalyst Release 默认的 AOT-only 模式不能加载外部 IL，因此 Host 在该目标上显式启用 Mono interpreter 并关闭托管程序集裁剪。若发行渠道是 Mac App Store，还需要单独评估性能、包体与动态插件审核政策；Windows 的 CoreCLR 插件模式不受此约束。

排查启动加载时可设置 `QUANTUM_PLUGIN_DIAGNOSTICS=1`，宿主会向标准输出写入插件加载与生命周期结果。

## 插件开发

完整流程见 [如何开发插件](docs/如何开发模块.md)，架构边界见 [Quantum 2.0 架构](docs/architecture-2.0.md)。可直接从 [Quantum.ExamplePlugin](samples/Quantum.ExamplePlugin) 复制起步。

## 插件市场

[Quantum Extension Market](quantum-extension-market/README.md) 已从原独立项目迁入，重构为 .NET 10 + NOF 的 Domain/Contract/Application/Infrastructure/Host 分层。用户、插件、版本、审核、下载、兼容性和审计 Contract 统一通过 `/rpc` 的 JSON-RPC 2.0 暴露；PostgreSQL、JWT、ZIP 安全校验和 Docker 部署说明均在子项目文档中。

## 许可证

MIT，详见 [LICENSE](LICENSE)。
