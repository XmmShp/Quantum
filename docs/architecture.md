# Quantum 架构

## 组件结构

```text
Quantum (NOF MAUI Host)
    ├── Quantum.Infrastructure
    │       └── Quantum.Application
    │               └── Quantum.Domain
    ├── Quantum.Plugin.Abstraction
    └── plugin runtimes (isolated ALC + DI container)
```

- Domain 只表达插件标识、SemVer、依赖、权限、页面声明与安装状态。
- Contract 放置市场、账号、版本和管理接口使用的传输模型。
- Application 负责依赖计划、加载结果和运行目录，不依赖 MAUI 或文件系统实现。
- Infrastructure 是可独立测试的 `net10.0` 层，负责 JSON、目录扫描、程序集解析和 WebView 文件提供器，不依赖 MAUI。
- `Quantum` 是 NOF MAUI 可执行宿主和组合根，负责桌面生命周期与 Blazor UI。
- `sdk/dotnet/src/Quantum.Plugin.Abstraction` 是宿主与插件共享的唯一 .NET SDK 和稳定 ABI，独立于宿主与 Infrastructure；程序集名、包名和命名空间均为单数形式 `Quantum.Plugin.Abstraction`，是插件兼容性边界。
- `quantum-extension-market` 是独立部署的 NOF Web Host，密码哈希、文件存储、JWT 与 EF Core 持久化均由该宿主组合；其 Contract 通过 `/rpc` 的 JSON-RPC 2.0 暴露，不进入桌面插件 ABI。

## 启动顺序

1. `NOFMauiAppBuilder.Create()` 创建 NOF/MAUI composition root。
2. 扫描 `Modules/*/plugin.json`，执行 schema 与文件预校验。
3. 按 `dependencies` 删除强前置缺失、版本不满足和硬循环的候选及其下游。
4. 对剩余插件拓扑排序；兼容且已安装的 `integrations` 形成软排序偏好，但不形成加载门槛。
5. 把每个候选复制到本次进程专属的影子目录；每代运行时创建独立的 collectible `PluginLoadContext`，入口 DLL 以流方式加载，依赖仍按影子路径解析。
6. 读取 NOF 生成的 `IAssemblyInitializer`，把 `AutoInject` 服务注册到插件私有 DI 容器；宿主根容器不保存插件 `Type`。
7. 为候选代建立私有环境快照并按依赖顺序执行 `IQuantumPlugin.StartAsync`；全部成功后再原子发布到宿主 `PluginCatalog`。
8. MAUI 创建 `BlazorWebView`；路由、菜单、静态文件提供器和 Web 注入订阅同一份动态 `PluginCatalog`。

加载失败按插件隔离；强前置失败的下游插件不会继续加载。弱联动缺失、版本不兼容或形成软循环时，插件仍然加载，规划器只放弃无法满足的顺序偏好。最终状态通过 SDK 的 `IQuantumPluginEnvironment` 暴露，插件可在启动时选择独立模式或联动模式。

## ALC 边界

每个插件拥有自己的 collectible ALC。Windows 使用 `AssemblyDependencyResolver`；Mac Catalyst 不提供该 API，因此按插件目录中的同名 DLL/本机库进行确定性解析。两种平台遵循相同边界：

1. `System.*`、`Microsoft.*`、`NOF.*`、`Quantum.Plugin.Abstraction` 和 `Quantum.Contract` 委托给默认上下文。
2. 其余托管依赖优先由插件目录解析。
3. 本机库由插件自己的 resolver 解析。

共享 ABI 是类型一致性的必要条件。若插件私载一份 `IQuantumPlugin` 所在程序集，即使命名空间与类型名相同，宿主仍无法把它识别为同一接口。

卸载时先逆序执行 `StopAsync`，随后从目录快照撤销路由、静态资源和 Web 贡献，释放插件 DI 容器，清除 Blazor 的组件类型缓存，最后调用 `AssemblyLoadContext.Unload`。实际内存回收仍由 GC 决定；插件必须在 `StopAsync` 中停止后台任务、解绑宿主事件并释放 JS/.NET 引用，不能把自己的类型写入宿主或第三方库的进程级静态缓存。

## 卸载与热升级事务

运行时始终从影子副本执行，因此 Windows 上的 `Modules` 源 DLL 不会被入口程序集锁定。热升级或重新扫描按以下事务执行：

1. 从当前 `Modules` 重新读取所有 manifest，并创建完整的新影子快照、ALC 和私有 DI 容器。
2. 新快照无法完整装载时直接丢弃，旧运行时不受影响。
3. 旧生命周期按依赖逆序停止，因此目标插件的所有下游强依赖会先于目标停用；任一停止钩子失败则取消切换，并重新启动已停止的旧生命周期。
4. 新生命周期针对候选环境快照按依赖顺序启动；此时宿主路由与 UI 仍指向旧目录。
5. 任一新启动钩子失败时丢弃候选代并重新启动旧快照；全部成功后才原子发布新目录。
6. 提交成功后释放旧容器、请求旧 ALC 卸载，并延迟清理仍被 GC 引用的影子目录。

单插件热升级会重建并重启完整插件快照，使强依赖和弱联动在新版本下重新求值；下游强依赖插件总是在目标之前停止、在目标之后恢复。运行时卸载不删除源文件。卸载目标存在直接或传递强依赖时，运行时先返回完整影响清单，只有显式确认后才级联卸载目标和清单中的插件；重新扫描可以恢复它们。

Mac Catalyst 默认 Release 为 AOT-only，外部 IL 程序集无法动态加载。`Quantum` 因此在 Catalyst 目标上强制 `UseInterpreter=true`，并以 `MtouchLink=None` 保留动态组件依赖的程序集与元数据；移除这些约束会导致原生运行时终止或裁剪后的无效程序。该选择会影响性能、包体和发行政策，面向 Mac App Store 前必须单独评审动态插件模式。Windows 目标继续使用常规 CoreCLR。

## WebView 静态文件

MAUI Blazor Hybrid 没有 ASP.NET Core 的 `UseStaticFiles`。Quantum 通过继承 `BlazorWebView.CreateFileProvider`，把宿主资源与插件物理目录组合为 `CompositeFileProvider`。插件资源的虚拟前缀固定为 `_content/{pluginId}`，避免将文件复制到宿主 `wwwroot`。

## 运行边界

- 只有桌面端加载 DLL 插件；移动端不执行插件程序集。
- 插件支持运行时卸载、重新扫描和事务式热升级；源文件的安装/删除仍由桌面端之外的安装流程负责。
- 桌面宿主从本地 `Modules` 目录加载插件，不直接执行市场下载和安装事务。
- HTML/JS 注入属于受信任代码边界，只应加载来源可信且经过审核的插件。
- 桌面宿主支持 Windows 与 macOS。
