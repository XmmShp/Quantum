# Quantum 架构

## 组件结构

```text
Quantum (单一 NOF MAUI Host 项目)
    ├── Plugins (模型、依赖规划、运行时、EventBus)
    ├── Components / WebPlugins / Platforms
    ├── Quantum.Plugin.Abstraction
    └── plugin runtimes (isolated ALC + DI container / sandboxed iframe)
```

- `Quantum` 在一个项目内包含桌面组合根、Blazor UI，以及 `Quantum.Plugins` 下的插件模型、依赖规划、manifest、ALC、文件系统和 EventBus 实现；这些内部职责以目录组织，不再拆成独立程序集。
- 同一个 `Quantum.csproj` 提供桌面 MAUI 目标和供测试、API 文档使用的普通 `net10.0` 核心目标；核心目标排除平台启动文件，但编译相同的插件实现源码。
- `sdk/dotnet/src/Quantum.Plugin.Abstraction` 是宿主与插件共享的唯一 .NET SDK 和稳定 ABI，独立于宿主实现；程序集名、包名和命名空间均为单数形式 `Quantum.Plugin.Abstraction`，是插件兼容性边界。
- manifest、运行时环境和 EventBus envelope 共用 SDK 中基于 NOF `IValueObject<string>` 生成的 `PluginId` 与
  `SemanticVersion` 值对象；Web adapter 保持
  字符串 JSON 形状，并在 TypeScript SDK 中映射为具有相同校验与比较语义的 branded string。
- `quantum-platform` 是独立部署的统一后端平台，平台级用户身份、权限、插件市场、密码哈希、文件存储、JWT 与 EF Core 持久化均由该宿主组合；其 Contract 通过 `/rpc` 的 JSON-RPC 2.0 暴露，不进入桌面插件 ABI。

## 启动顺序

1. `NOFMauiAppBuilder.Create()` 创建 NOF/MAUI composition root。
2. 扫描 `Modules/*/plugin.json`，执行 schema、runtime 入口与 SQL migration artifact 预校验。
3. 按 `dependencies` 删除强前置缺失、版本不满足和硬循环的候选及其下游。
4. 对剩余插件拓扑排序；兼容且已安装的 `integrations` 形成软排序偏好，但不形成加载门槛。
5. 把每个候选复制到本次进程专属的影子目录；.NET runtime 创建独立的 collectible `PluginLoadContext`，Web runtime 则生成独立 iframe descriptor。
6. .NET runtime 读取 NOF 生成的 `IAssemblyInitializer` 并建立私有 DI 容器；Web runtime 先验证 `wwwroot` 下的单文件 ESM 并生成 descriptor。
7. 为候选代建立私有环境快照和运行期 DI scope；Host 按依赖顺序先应用该插件尚未执行的 SQL migrations，再从入口程序集发现静态 .NET `IQuantumPlugin` bootstrap 并执行 `StartAsync`。bootstrap 不进入 DI，全部成功后再原子发布到宿主 `PluginCatalog`。
8. MAUI 创建 `BlazorWebView`；路由、菜单、静态文件提供器和 Web 注入订阅同一份动态 `PluginCatalog`。Web descriptor 发布后，宿主才在 opaque-origin iframe 内通过 Blob Module 激活入口。

加载失败按插件隔离；强前置失败的下游插件不会继续加载。弱联动缺失、版本不兼容或形成软循环时，插件仍然加载，
规划器只放弃无法满足的顺序偏好。`integrations` 不构成运行时调用权限；最终已加载状态通过 SDK 的
`IQuantumPluginEnvironment` 暴露，插件可以在没有 integration 声明时按运行环境选择独立模式或联动模式。

## 插件数据库 migration

`.NET` 和 Web runtime 共享 manifest 的 `database.migrations` 能力。Host 只消费插件包中的 SQLite SQL，不加载 EF、
Prisma 或 Drizzle migration runtime。文件用数字前缀确定顺序，每个发布包必须保留完整的追加式历史。Host 的
`__quantum_plugin_migrations` 表以 `plugin_id + migration_name` 为键保存 SHA-256、发布版本和时间；已应用前缀与当前
artifact 不一致时拒绝启动插件。

一个插件本次所有待应用脚本与历史写入处于同一个 SQLite 事务中，并发生在插件 runtime 生命周期启动之前。migration
提交后是 forward-only 的：后续插件生命周期或其他候选启动失败时，运行时代可以切回旧代码，但数据库不会执行降级。
因此发布者必须使用向后兼容的 expand/migrate/contract 方案。Host 管理事务边界，artifact 不能包含事务控制语句。

Web runtime 不把宿主对象直接暴露给 iframe。iframe 只能通过经来源校验的 `postMessage` 与宿主通信；父页面再通过
`DotNetObjectReference` 转发到 capability RPC。`.NET` 调用按次创建 DI scope，可访问宿主或任意已加载 .NET 插件的服务 FQN，
并在异步结果完成且序列化之后释放 scope。销毁 iframe 会强制终止未正确清理的定时器、事件和模块全局状态。

.NET 插件之间只为 manifest 中声明的直接强依赖建立服务桥接：Host 把被依赖插件的运行期 provider 以其 `PluginId`
注册为调用方容器中的 keyed `IServiceProvider`。调用方可按服务 FQN 解析并通过 `dynamic` 调用；依赖顺序同时保证服务
提供方先启动、后停止。provider 与服务实例的所有权仍属于提供方，调用方不能释放或跨运行代缓存。

## 插件 EventBus

.NET 插件容器与 TypeScript iframe 都获得带当前插件身份的 EventBus。Topic 在 .NET 中是 NOF
`IValueObject<string>`，在 TypeScript 中是由 `QuantumTopic.of()` 创建的 branded string；两端使用相同的点分层级、
255 长度上限和 `^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$` 格式校验。Host envelope 与路由表
始终持有 .NET `QuantumTopic`。EventBus 不是 ROS wire protocol，也不提供进程外传输、持久化、队列深度或历史重放。

发布时，插件消息立即序列化成只包含 JSON、Topic、事件 ID、时间与发布插件身份的稳定 Host envelope。Host 在新的
DI scope 内通过 NOF `IEventPublisher` 发布 envelope，由单一的
`InMemoryEventHandler<PluginEventTransportMessage>` 转发给 Topic 当前订阅者。NOF registry 因而只持有 Host
稳定类型，不持有动态插件类型。订阅端统一取得只含原始 `JsonElement` 的 `QuantumEvent`，再在自己的运行代边界内
按需反序列化成目标消息类型。publisher 的 `TMessage` 不参与路由，也不会作为 CLR 类型标识写入 envelope。这允许
不同 ALC 使用结构兼容、但 CLR 类型身份不同的 DTO，同时避免破坏 collectible ALC。

Web adapter 通过 capability RPC 完成 publish/subscribe/unsubscribe。Host 为每个 `pluginId + runtimeId` 创建一个带
所有权的 EventBus，并通过父页面把 `QuantumEvent` 推送到对应 sandbox iframe；每次投递带独立 delivery id，Host 会
等待 iframe handler 返回成功或失败确认，因此仍保持 `PublishAsync` 的等待和错误聚合语义。正常 deactivate 会主动
退订，父页面销毁 iframe 时还会调用 Host 释放整个 runtime EventBus，覆盖插件脚本异常退出的情况。

插件运行代在 `StartAsync` 前恢复 EventBus，在成功 `StopAsync` 后暂停；停止失败时保持旧运行代状态，候选启动失败时
暂停候选总线，回滚启动会再次恢复旧总线。最终释放插件 DI 容器会清除该运行代全部 Host 订阅委托。插件生命周期仍应
主动释放 subscription，以免一次运行代内的 stop/start 回滚产生重复订阅。总线释放时还会清除该运行代的 JSON
metadata，以及 System.Text.Json 用于动态 DTO 的 member-accessor 缓存，避免缓存委托阻止 collectible ALC 回收。

## ALC 边界

每个插件拥有自己的 collectible ALC。Windows 使用 `AssemblyDependencyResolver`；Mac Catalyst 不提供该 API，因此按插件目录中的同名 DLL/本机库进行确定性解析。两种平台遵循相同边界：

1. `System.*`、`Microsoft.*`、`NOF.*` 和 `Quantum.Plugin.Abstraction` 委托给默认上下文。
2. 其余托管依赖优先由插件目录解析。
3. 本机库由插件自己的 resolver 解析。

共享 ABI 是类型一致性的必要条件。若插件私载一份 `IQuantumPlugin` 所在程序集，即使命名空间与类型名相同，宿主仍无法把它识别为同一接口。

卸载时先逆序执行 `StopAsync`，随后从目录快照撤销路由、静态资源和 Web 贡献，释放插件 DI 容器，清除 Blazor 的组件类型缓存，最后调用 `AssemblyLoadContext.Unload`。实际内存回收仍由 GC 决定；插件必须在 `StopAsync` 中停止后台任务、解绑宿主事件并释放 JS/.NET 引用，不能把自己的类型写入宿主或第三方库的进程级静态缓存。

## 禁用、卸载与热升级事务

运行时始终从影子副本执行，因此 Windows 上的 `Modules` 源 DLL 不会被入口程序集锁定。热升级或重新扫描按以下事务执行：

1. 从当前 `Modules` 重新读取所有 manifest，并创建完整的新影子快照、ALC 和私有 DI 容器。
2. 新快照无法完整装载时直接丢弃，旧运行时不受影响。
3. 旧生命周期按依赖逆序停止，因此目标插件的所有下游强依赖会先于目标停用；任一停止钩子失败则取消切换，并重新启动已停止的旧生命周期。
4. 新 runtime 针对候选环境快照按依赖顺序应用待执行 migration 并启动生命周期；此时宿主路由与 UI 仍指向旧目录。
5. 任一新启动钩子失败时丢弃候选代并重新启动旧快照；全部成功后才原子发布新目录。
6. 提交成功后释放旧容器、请求旧 ALC 卸载，并延迟清理仍被 GC 引用的影子目录。SQL migration 一旦提交不会随 runtime 回滚而降级。

单插件热升级会重建并重启完整插件快照，使强依赖和弱联动在新版本下重新求值；下游强依赖插件总是在目标之前停止、在目标之后恢复。禁用目标存在直接或传递强依赖时，运行时先返回完整影响清单，只有显式确认后才把目标和清单中的插件目录移动到保留目录 `Modules/disabled`；运行时切换失败会把所有目录移回。重新启用采用相反的目录事务，并重新验证完整快照。卸载同样先确认影响清单，但提交后会物理删除插件目录，不保留可供重新扫描恢复的源文件。

Mac Catalyst 默认 Release 为 AOT-only，外部 IL 程序集无法动态加载。`Quantum` 因此在 Catalyst 目标上强制 `UseInterpreter=true`，并以 `MtouchLink=None` 保留动态组件依赖的程序集与元数据；移除这些约束会导致原生运行时终止或裁剪后的无效程序。该选择会影响性能、包体和发行政策，面向 Mac App Store 前必须单独评审动态插件模式。Windows 目标继续使用常规 CoreCLR。

## WebView 静态文件

MAUI Blazor Hybrid 没有 ASP.NET Core 的 `UseStaticFiles`。Quantum 通过继承 `BlazorWebView.CreateFileProvider`，把宿主资源与插件物理目录组合为 `CompositeFileProvider`。插件资源的虚拟前缀固定为 `_content/{pluginId}`，避免将文件复制到宿主 `wwwroot`。

## 运行边界

- 只有桌面端加载 DLL 插件；移动端不执行插件程序集。
- 插件支持禁用、重新启用、物理卸载、重新扫描和事务式热升级；`Modules/disabled` 不参与普通发现，`disabled` 也是保留插件 id。
- 桌面宿主从本地 `Modules` 目录加载插件，并支持在整个应用窗口拖拽 ZIP 整合包执行预检确认后的文件安装事务。
- HTML/JS 注入属于受信任代码边界，只应加载来源可信且经过审核的插件。
- 桌面宿主支持 Windows 与 macOS。
