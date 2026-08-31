# Quantum .NET Plugin SDK

`Quantum.Plugin.Abstraction` 是 Quantum 唯一的插件 SDK，也是桌面宿主与本地 DLL 插件共享的稳定 ABI。

生产插件应引用发布后的 `Quantum.Plugin.Abstraction` NuGet 包。仓库内的样例使用项目引用，以便 SDK、宿主与样例一起构建和验证。
