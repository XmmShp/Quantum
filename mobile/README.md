# Quantum Mobile 0.1 开发预览

原生 MAUI 伴随客户端。Android/iOS 只运行内置控件 Renderer；Windows/Mac Catalyst 目标充当 SDK 模拟器。所有真实业务在桌面插件内执行，两端通过已登录的 Quantum Platform 短轮询收发，不需要互相直连。

## 当前实现

- 平台登录、设备注册、同账号设备发现、桌面确认授权、插件页面选择。
- 原生基础布局、纯文本、输入框、开关、按钮、分隔、留白与忙碌控件。
- 桌面 SDK 数据式 Fluent API，.NET 与 TypeScript 实际 Host transport。
- 手机仅上传固定 actionId 与声明的输入；桌面固定目标 endpoint/runtime，复用现有插件 RPC。
- 2 秒轮询、租约与 Ack、重复请求校验、执行前平台再次授权、桌面持久化执行记录和结果 outbox。
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

使用 Debug 配置启动 Windows 目标，选择“打开模拟器 · Mock 模式”，输入文字、切换开关并点保存。结果会明确标记 MOCK，不触发桌面业务。

选择“模拟器 · 打开 UI JSON”可打开 `mobile/fixtures/mobile-page-v1.json`。文件中的固定 RPC 会被替换成 Mock 动作，仅采集对应输入。没有脚本或 DLL 加载路径。

## 联调真实桌面

1. 启动包含此次变更的 Quantum Platform。准备本地 PostgreSQL 和项目原有配置；启动自动迁移新增的 `MobileBridgeAccount` 表。不要把测试命令指向生产数据库。
2. 桌面配置 `QUANTUM_PLATFORM_URL` 指向该平台，再启动桌面 Quantum；在插件市场登录。
3. 打开左侧“移动访问”（`/mobile`），选择提供移动页面的插件，点击“开启共享”。示例插件提供 “Quantum on your phone” 页面，按钮调用现有 `example.greet` RPC。
4. 手机/模拟器输入同一平台 URL，用同一账号登录；选择电脑并申请访问。
5. 核对手机上显示的设备 ID 前 8 位，在桌面移动访问页允许该手机。
6. 手机刷新设备列表，进入已授权电脑与共享页面。点击按钮后查看 NOF 结果；修改声明或执行了业务后可显式刷新页面。
7. 停止共享会先关闭桌面执行入口再撤销设备；手机授权可单独撤销。凭据只在宿主安全存储，不提供给插件。

默认平台 `https://quantum.io-vii.com/` 已部署移动中转：账号登录走 `/rpc`，移动控制面走 `/rpc/mobile`。桌面和手机使用同一平台账号，仍须手动开启共享并批准设备。除 localhost 开发地址外要求 HTTPS；Android 默认不允许明文 HTTP，也不提供跨设备局域网直连入口。

## 与完整设计稿的差异

这是一版功能闭环预览，还不是生产发行：

- 页面通过 Publish/Remove 管理，刷新获取全量快照；未实现按会话异步 provider、自动失效推送、动态语言重建与业务成功后的自动页面刷新。
- 平台先使用每账号有界 JSON 邮箱聚合 + PostgreSQL 账号锁和并发版本，不是面向大规模 QPS 的独立消息表；每账号最多 20 个设备、2,000 条保留消息、10 MiB 聚合数据。TTL 在账号请求时清理，尚无空闲账号定时正文清理 Worker。
- 设备凭据最长 24 小时，需重新登录/注册；没有新增 RefreshToken 或推送。注册设备的 session 在进程崩溃恢复时复用，页面动作仍由内存 viewId/runtime 绑定阻止旧页面执行。
- 界面与动作 5 分钟有效；命令 30 秒内未授权启动即过期。消息 ID、执行记录和结果首期保留 24 小时，不是技术稿建议的 7 天去重窗口；更早的命令因到期/视图失效不能重做。
- 不保证 exactly-once。执行中崩溃、超时或超大结果显示 unknown；用户需核对桌面。首期不保证在超时后继续追踪已脱离等待的 Handler 的最终结果。
- 自动化覆盖纯协议、平台邮箱状态机、实际 iframe Host 调用、真实插件 RPC 以及通过 HttpMessageHandler 的完整收发闭环。另外已在真实平台 PostgreSQL 上应用迁移，并经 Android 模拟器、公网 HTTPS、本机桌面真实插件跑通调用。
- Android 模拟器已完成实际安装、登录、设备授权、页面渲染和 RPC 结果显示；Windows 编译和启动探测通过。iOS、实体手机生命周期、完整无障碍验收与 App Store 审核尚待验证。

进一步背景参见 [需求稿](../docs/mobile-requirements.md)、[技术设计稿](../docs/mobile-technical-design.md)与 [SDK 移动 UI 说明](https://github.com/XmmShp/Quantum.Sdk/blob/main/docs/mobile-ui.md)。

## 官方插件适配

Calendar、Ledger 和 Codex 的移动适配与接入步骤见[官方插件移动端说明](https://github.com/XmmShp/Quantum.OfficialPlugins/blob/main/docs/mobile.md)。需要构建对应的适配版本，旧市场安装包不会自动出现移动页面。

## 登录与连接设置

平台账号与开发者后台使用同一套邮箱和密码；登录使用邮箱，不使用用户名或插件发布凭证。登录页右上角可切换简体中文/English，选择会保存在本机。登录后若显示“选择你的电脑”，说明已进入设备选择阶段；在电脑上使用同一账号打开“移动访问”并开始共享，手机再刷新设备列表并请求授权。

默认连接官方 Quantum 平台。自部署平台的 Base URL 位于二级“高级设置”页面，保存前会校验 HTTPS（仅 localhost 允许 HTTP）；应填写平台根地址，而不是 /portal 或 /rpc。Base URL 会保存在本机，下次登录继续使用。

Mock 示例和 UI JSON 导入仅出现在 Debug 构建中。Release 登录页不包含这些开发工具；Windows Release 模拟器与发布给手机的版本采用同一规则。
