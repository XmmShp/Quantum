# Quantum Mobile 0.1 开发预览

原生 MAUI 伴随客户端。Android/iOS 只运行内置控件 Renderer；Windows/Mac Catalyst 目标充当 SDK 模拟器。所有真实业务在桌面插件内执行，两端通过已登录的 Quantum Platform 短轮询收发，不需要互相直连。

## 当前实现

- 平台登录、设备注册、同账号电脑直连、自动同步全部移动插件页面、可上拉的底部插件导航。
- 原生基础布局、纯文本、输入框、开关、按钮、分隔、留白与忙碌控件。
- 桌面 SDK 数据式 Fluent API，.NET 与 TypeScript 实际 Host transport。
- 手机仅上传固定 actionId 与声明的输入；桌面固定目标 endpoint/runtime，复用现有插件 RPC。
- 500 毫秒轮询、租约与 Ack、重复请求校验、执行前平台再次授权、桌面持久化执行记录和结果 outbox。
- 断线/退后台禁用操作；过期页面须刷新；未知结果不自动重试，可在核对桌面后清除待确认状态。
- Windows 模拟器内置 Mock 示例和本地 UI JSON 导入，使用与手机相同的控件渲染器。

## 构建

桌面、示例插件使用公开的 `Quantum.Plugin.Abstraction` NuGet 包；Web 示例使用同版本的 `@io7/quantum-plugin-sdk` npm 包。构建本仓库无需检出 Quantum.Sdk 或 Quantum.Platform 源码。

移动通信模块位于 `quantum/src/Quantum.Mobile.Internal`，仅供桌面、平台和移动应用内部共享，设置 `IsPackable=false`，不公开发包；移动应用不引用插件 SDK。平台源码构建需要该内部模块源码，这与插件 SDK 的公共包依赖相互独立。

本机要求所有 dotnet 命令在提升权限的终端执行。从 Quantum 仓库运行：

```powershell
# Windows 模拟器
dotnet run --project mobile/Quantum.Mobile/Quantum.Mobile.csproj -f net10.0-windows10.0.19041.0

# Android Debug APK（要求 Android SDK/JDK/对应 workload）
dotnet build mobile/Quantum.Mobile/Quantum.Mobile.csproj -f net10.0-android

# 桌面核心及集成测试
dotnet test quantum/tests/Quantum.Tests/Quantum.Tests.csproj -f net10.0 -p:TargetFrameworks=net10.0
```

Android 调试包在 `mobile/Quantum.Mobile/bin/Debug/net10.0-android/io.quantum.mobile-Signed.apk`。已启用 EmbedAssembliesIntoApk，包内包含所需程序集，可脱离 IDE 直接安装；它使用开发签名，不是商店发行包。iOS 构建需 macOS、Xcode、MAUI iOS workload 与相应签名；Windows 环境未验证 iOS。

## 不登录体验模拟器

开发自动化可使用 [DEBUG 本地测试接口](debug-interface.md)：通过 HTTP 读取页面树、设置输入并触发按钮，无需频繁接管键鼠。接口需显式启用、仅监听 loopback，并在 Release 构建中排除。

使用 Debug 配置启动 Windows 目标，选择“打开模拟器 · Mock 模式”，输入文字、切换开关并点保存。结果会明确标记 MOCK，不触发桌面业务。

选择“模拟器 · 打开 UI JSON”可打开 `mobile/fixtures/mobile-page-v1.json`。文件中的固定 RPC 会被替换成 Mock 动作，仅采集对应输入。没有脚本或 DLL 加载路径。

## 联调真实桌面

1. 启动包含此次变更的 Quantum Platform。准备本地 PostgreSQL 和项目原有配置；启动自动迁移新增的 `MobileBridgeAccount` 表。不要把测试命令指向生产数据库。
2. 桌面配置 `QUANTUM_PLATFORM_URL` 指向该平台，再启动桌面 Quantum；在插件市场登录。
3. 桌面登录后自动上线并同步所有插件提供的移动页面，不需要勾选或开启共享。“移动访问”（`/mobile`）可查看连接状态和可用页面。
4. 手机/模拟器使用同一平台 URL 和账号登录，无需申请或批准。完成登录后恢复该账号上次使用的电脑；首次登录按菜单顺序进入第一台在线电脑。设备偏好按平台与账号隔离，退出登录后仍可恢复。上次电脑离线时等待它上线，不会静默切换到另一台；注册 ID 变化时只在同账号内存在唯一同名电脑时恢复，重名则由用户在菜单选择。
5. 连接后自动打开第一个插件页面。底部导航最多固定四个插件图标，图标取自桌面导航声明；上拉底栏展开四列图标矩阵，每格显示图标和名称。长按图标或列表项再拖动可调整顺序，前四个进入底栏；顺序按平台账号及电脑保存，切换其它页面不会自动替换固定位置。
6. 页面变化自动同步，操作结果立即上传。插件页顶部仅保留 44 高度的标题栏和头像；点击头像从左侧打开账户菜单，上半区显示用户与当前电脑，可展开切换设备，设置固定在菜单底部。内容区使用原生下拉刷新：到达阈值松手触发，刷新完成或未达阈值则回弹；刷新会更新设备、插件目录和当前页面，并等待新快照显示后结束指示器。设置中不提供连接状态或刷新按钮，正常状态不占用视口；插件页面仍从底部导航切换。切换插件先显示会话内缓存，再更新有效动作；未提交草稿会在切换前确认。
7. 桌面退出账号会停止移动连接并关闭动作入口。设备凭据只在宿主安全存储，不提供给插件；不同账号的设备和消息始终隔离。

默认平台 `https://quantum.io-vii.com/` 已部署移动中转：账号登录走 `/rpc`，移动控制面走 `/rpc/mobile`。桌面和手机使用同一平台账号，登录后即可直连，不再要求额外设备授权。除 localhost 开发地址外要求 HTTPS；Android 默认不允许明文 HTTP，也不提供跨设备局域网直连入口。

## 与完整设计稿的差异

这是一版功能闭环预览，还不是生产发行：

- 页面通过 Publish/Remove 管理，桌面订阅发布版本并推送变化；官方插件每 2 秒读取一次状态。两端以 500 毫秒间隔轮询，并非 WebSocket 流；正常网络下更新通常在数秒内抵达。控件按 ID 更新以保留草稿、焦点和滚动位置，提交成功后才重置输入。动态语言声明仍由桌面插件负责。
- 平台只保留每页最新待取快照，确认后的页面消息会清理；最多保留三个有效视图供在途动作校验，业务命令和结果仍保留 24 小时防重记录。
- 平台先使用每账号有界 JSON 邮箱聚合 + PostgreSQL 账号锁和并发版本，不是面向大规模 QPS 的独立消息表；每账号最多 20 个设备、2,000 条保留消息、10 MiB 聚合数据。TTL 在账号请求时清理，尚无空闲账号定时正文清理 Worker。
- 平台登录支持自动续期和安全存储恢复，登录会话最长 30 天；设备注册凭据保留 24 小时，到期重新连接即可，不需要批准。注册设备的 session 在进程崩溃恢复时复用，页面动作仍由内存 viewId/runtime 绑定阻止旧页面执行。详见[登录会话说明](../docs/login-sessions.md)。
- 界面与动作 5 分钟有效；打开页面请求 30 秒内未处理即过期，业务命令可在原页面有效期内排队，过期后不再新启动。消息 ID、执行记录和结果首期保留 24 小时，不是技术稿建议的 7 天去重窗口；更早的命令因到期/视图失效不能重做。
- 不保证 exactly-once。执行中崩溃、超时或超大结果显示 unknown；用户需核对桌面。首期不保证在超时后继续追踪已脱离等待的 Handler 的最终结果。
- 自动化覆盖纯协议、平台邮箱状态机、实际 iframe Host 调用、真实插件 RPC 以及通过 HttpMessageHandler 的完整收发闭环。另外已在真实平台 PostgreSQL 上应用迁移，并经 Android 模拟器、公网 HTTPS、本机桌面真实插件跑通调用。
- Android 模拟器已完成实际安装、登录、设备授权、页面渲染和 RPC 结果显示；Windows 编译和启动探测通过。iOS、实体手机生命周期、完整无障碍验收与 App Store 审核尚待验证。

进一步背景参见 [需求稿](../docs/mobile-requirements.md)、[技术设计稿](../docs/mobile-technical-design.md)与 [SDK 移动 UI 说明](https://github.com/XmmShp/Quantum.Sdk/blob/main/docs/mobile-ui.md)。

## 官方插件适配

Calendar、Ledger 和 Codex 的移动适配与接入步骤见[官方插件移动端说明](https://github.com/XmmShp/Quantum.OfficialPlugins/blob/main/docs/mobile.md)。需要构建对应的适配版本，旧市场安装包不会自动出现移动页面。

## 登录与连接设置

平台账号与开发者后台使用同一套邮箱和密码；登录使用邮箱，不使用用户名或插件发布凭证。登录页右上角可切换简体中文/English，选择会保存在本机。登录后若显示“选择你的电脑”，说明已进入设备选择阶段；确认电脑上的 Quantum 已登录同一账号并保持运行，手机刷新设备列表后直接选择电脑。

默认连接官方 Quantum 平台。自部署平台的 Base URL 位于二级“高级设置”页面，保存前会校验 HTTPS（仅 localhost 允许 HTTP）；应填写平台根地址，而不是 /portal 或 /rpc。Base URL 会保存在本机，下次登录继续使用。

Mock 示例和 UI JSON 导入仅出现在 Debug 构建中。Release 登录页不包含这些开发工具；Windows Release 模拟器与发布给手机的版本采用同一规则。

## 断连与即时反馈

点击业务按钮后先将完整命令、原始请求 ID 和输入写入 SecureStorage，再立即清空已提交字段并显示短暂的发送状态。排队时禁用重复提交，输入框仍可编辑下一份草稿；成功不会覆盖新输入，明确失败可恢复原输入。连接状态不再占据常驻视口。

平台邮箱持久化接收短暂离线桌面的有效命令。手机恢复连接后只补发同一请求 ID、同一内容；平台去重、执行前授权和桌面执行日志共同避免重做。执行结果未知时保留待核对状态。页面有效期、插件 runtime 和动作声明仍须在执行前通过检查，过期命令不会无限积压。

设备使用按安装、平台与账号隔离的稳定标识；正常关闭桌面保留注册，退出账号仍撤销。切换菜单只显示在线电脑，同名在线电脑显示短 ID 以区分，不按名称合并真实设备。
