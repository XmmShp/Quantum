# Quantum .NET Plugin SDK

`Quantum.Plugin.Abstraction` 是 Quantum 唯一的插件 SDK，也是桌面宿主与本地 DLL 插件共享的稳定 ABI。

生产插件应引用发布后的 `Quantum.Plugin.Abstraction` NuGet 包。仓库内的样例使用项目引用，以便 SDK、宿主与样例一起构建和验证。

- `IQuantumPlugin` 定义插件启动和停止生命周期；`StopAsync` 有兼容旧插件的默认空实现。
- `IQuantumPluginEnvironment` 提供已加载插件列表，并允许插件判断 manifest 中声明的弱联动是否已经激活。
- `IQuantumPluginRuntimeContext` 提供当前运行版本和只读影子目录路径。
