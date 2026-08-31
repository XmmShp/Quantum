# Quantum 2.0 架构基线

## 目标结构

```text
Quantum.Host (MAUI)
    ├── Quantum.Infrastructure
    │       └── Quantum.Application
    │               ├── Quantum.Domain
    │               └── Quantum.Plugin.Abstraction
    └── plugin Application Parts (isolated ALC)
```

- Domain 只表达插件标识、SemVer、依赖、权限、页面声明与安装状态。
- Contract 放置市场、账号、版本和管理接口使用的传输模型。
- Application 负责依赖计划、加载结果和运行目录，不依赖 MAUI 或文件系统实现。
- Infrastructure 负责 JSON、目录扫描、程序集解析和 WebView 文件提供器。
- Host 负责 NOF composition root、桌面生命周期和 Blazor UI。
- `sdk/Quantum.Plugin.Abstraction` 是宿主与插件共享的唯一 .NET SDK 和稳定 ABI，独立于 Host 和 Infrastructure；程序集名、包名和命名空间均为单数形式 `Quantum.Plugin.Abstraction`，是插件兼容性边界。
- `quantum-extension-market` 是独立部署的 NOF Web Host；其市场、账号、版本、审核和审计 Contract 统一通过 `/rpc` 的 JSON-RPC 2.0 暴露，不进入桌面插件的 ABI。

## 启动顺序

1. `NOFMauiAppBuilder.Create()` 创建 NOF/MAUI composition root。
2. 扫描 `Modules/*/plugin.json`，执行 schema 与文件预校验。
3. 删除缺失依赖、版本不满足和重复 ID 的候选；对剩余候选拓扑排序。
4. 每个候选创建独立的 collectible `PluginLoadContext`，加载入口 DLL 并解析页面类型。
5. 每个成功程序集调用 `AddApplicationPart`，由 NOF 注册 `AutoInject` 服务和 Handler。
6. `BuildAsync` 执行 NOF 服务注册与 Initialization Step。
7. MAUI 创建 `BlazorWebView`；Router、菜单、静态文件提供器和 Web 注入读取同一份只读 `PluginCatalog`。

加载失败按插件隔离；依赖失败的下游插件不会继续加载。相互依赖的插件会被标记为循环依赖。

## ALC 边界

每个插件拥有自己的 collectible ALC。Windows 使用 `AssemblyDependencyResolver`；Mac Catalyst 不提供该 API，因此按插件目录中的同名 DLL/本机库进行确定性解析。两种平台遵循相同边界：

1. `System.*`、`Microsoft.*`、`NOF.*`、`Quantum.Plugin.Abstraction` 和 `Quantum.Contract` 委托给默认上下文。
2. 其余托管依赖优先由插件目录解析。
3. 本机库由插件自己的 resolver 解析。

共享 ABI 是类型一致性的必要条件。若插件私载一份 `IQuantumPlugin` 所在程序集，即使命名空间与类型名相同，宿主仍无法把它识别为同一接口。

虽然 ALC 设为 collectible，V1 不调用 `Unload`。Blazor 组件类型、DI descriptor、事件和 JS 引用都可能持有程序集；真正热卸载需要完整的引用追踪与回收验证。

Mac Catalyst 默认 Release 为 AOT-only，外部 IL 程序集无法动态加载。`Quantum.Host` 因此在 Catalyst 目标上强制 `UseInterpreter=true`，并以 `MtouchLink=None` 保留动态 Application Part 依赖的程序集与元数据；移除这些约束会导致原生运行时终止或裁剪后的无效程序。该选择会影响性能、包体和发行政策，面向 Mac App Store 前必须单独评审动态插件模式。Windows 目标继续使用常规 CoreCLR。

## WebView 静态文件

MAUI Blazor Hybrid 没有 ASP.NET Core 的 `UseStaticFiles`。Quantum 通过继承 `BlazorWebView.CreateFileProvider`，把宿主资源与插件物理目录组合为 `CompositeFileProvider`。插件资源的虚拟前缀固定为 `_content/{pluginId}`，避免将文件复制到宿主 `wwwroot`。

## 已知边界

- 只有桌面端加载插件；移动端若加入产品，只消费桌面端产生的结果。
- 插件更新和卸载需要重启。
- 当前启动扫描只读取本地目录，不包含市场下载、压缩包事务、原子替换和签名验证。
- HTML/JS 注入当前假定插件已受信任。生产发行前必须把签名、审核结果和权限授权接入安装管线。
- MAUI 官方桌面目标是 Windows 与 macOS；Quantum 2.0 不再承诺 Electron 时代的 Linux 桌面外壳。
