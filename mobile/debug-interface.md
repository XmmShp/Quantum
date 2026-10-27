# DEBUG 本地测试接口

仅用于 Debug 构建，不属于插件 SDK，也不改变移动通信协议。接口直接调度 MAUI 主线程上的实际控件及原有页面操作，不激活窗口、不发送键鼠输入。Release 会排除服务端源码，发布脚本也会检查程序集不包含调试入口。

## Windows 启动

在提升权限的 PowerShell 7 终端、仓库根目录运行（本机所有 dotnet 命令要求提升权限）：

```powershell
$env:QUANTUM_MOBILE_DEBUG_PORT = '9224'
$env:QUANTUM_MOBILE_DEBUG_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
# 可选：直接进入隔离 Mock，不读取保存的登录、不连接平台。
$env:QUANTUM_MOBILE_DEBUG_PREVIEW = '1'
dotnet run --project mobile/Quantum.Mobile/Quantum.Mobile.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:QuantumMobileTargetFramework=net10.0-windows10.0.19041.0
```

未设置端口时不启动服务。必须提供至少 32 字符的调试令牌，服务仅绑定 `127.0.0.1`。在另一个终端设置相同的 `QUANTUM_MOBILE_DEBUG_TOKEN` 后使用下列命令。不要将令牌写入仓库、URL 或公开日志。

## 页面操作

```powershell
$state = ./scripts/Invoke-MobileDebug.ps1
$view = $state.value.page.viewId
$state.value.page.tree  # 节点 ID、类型、文字、有效启用状态、当前输入和子节点

./scripts/Invoke-MobileDebug.ps1 -Method POST -Path /input -Json (@{
    viewId=$view; nodeId='content'; value='debug test'
} | ConvertTo-Json)
./scripts/Invoke-MobileDebug.ps1 -Method POST -Path /input -Json (@{
    viewId=$view; nodeId='pinned'; value=$true
} | ConvertTo-Json)
./scripts/Invoke-MobileDebug.ps1 -Method POST -Path /press -Json (@{
    viewId=$view; nodeId='save'
} | ConvertTo-Json)
```

上述节点来自内置 Mock。真实插件应先读取页面树，使用其中的节点 ID。`/input` 改变实际 Entry/Switch，经过原有事件绑定；`/press` 调用实际按钮绑定，只提交该按钮声明的输入。不支持任意 RPC、脚本执行或文件读取。

| 方法与路径 | 用途 |
| --- | --- |
| `GET /state` | 页面树、状态、登录/连接布尔值、pending/uncertain 和当前调试 viewId |
| `GET /devices` | 已登录账号的在线电脑（不暴露其他账号设备） |
| `POST /desktop/select` | `{desktopId}`：选择同账号的在线电脑，无需审批 |
| `GET /pages` | 当前电脑共享的页面列表 |
| `POST /page/open` | `{pluginId,pageId,discardDraft?}`：打开实际共享页面 |
| `POST /page/refresh` | `{viewId,discardDraft?}`：刷新当前真实页面 |
| `POST /input` | `{viewId,nodeId,value}`：填写字符串或布尔输入 |
| `POST /press` | `{viewId,nodeId}`：触发当前页面按钮 |
| `POST /menu/open` | 打开头像对应的左侧账户菜单 |
| `POST /menu/close` | 收起账户菜单 |
| `POST /settings/open` | 从账户菜单进入独立设置页 |
| `POST /settings/close` | 返回插件页，保留草稿 |
| `POST /navigation/open` | 展开底部插件菜单 |
| `POST /navigation/close` | 收起底部插件菜单 |
| `POST /navigation/select` | `{pluginId,pageId,discardDraft?}`：切换插件，保留草稿保护 |
| `POST /preview` | 进入内置隔离 Mock；有未确认操作或草稿时拒绝 |
| `POST /preview/update` | 仅内置 Mock：模拟实时页面变化，验证原生输入、焦点和草稿保留 |
| `POST /preview/close` | 离开 Mock，尝试恢复已有登录；不接受密码 |

`viewId` 是每次渲染更新的调试标识。旧 viewId、禁用控件、过期页面、未确认操作、超长输入和错误输入类型都会被拒绝。刷新或换页遇到未提交草稿时返回 `409 debug_draft_conflict`，只有显式提供 `discardDraft:true` 才丢弃。打开和刷新采用正常平台邮箱通道，返回后应轮询 `/state` 等待新页面。

真实业务模式先去掉 `QUANTUM_MOBILE_DEBUG_PREVIEW` 并重启。已有安全存储登录会自动恢复；首次登录仍在正常界面完成；桌面自动提供全部移动页面，同账号无需设备审批。接口不暴露密码、平台访问/续期令牌或设备密钥，没有登录、批准、撤销和任意 RPC 路由。不要同时运行两个使用相同登录和设备注册的真实客户端实例。

POST 不自动重试。HTTP 超时或断开不能证明业务未执行，应查询 `/state` 的 pending/uncertain，并按正常流程在桌面核对结果。Mock 和真实模式都不会绕过这些边界。原生弹窗仍应由开发者处理；调试导航提前检测草稿冲突，避免为正常测试弹出丢弃确认框。

构建 Windows Debug 后，可运行 `./scripts/Test-MobileDebug.ps1` 自动完成隔离 Mock 的端到端检查。脚本临时生成调试令牌、启动独立客户端，通过 HTTP 验证原生绑定和按钮回调，最后关闭该测试进程并恢复环境变量；不会读取保存的登录或改动真实业务数据。它会启动一次模拟器窗口，之后无需激活窗口或使用键鼠。

## Android

Debug 应用还可从应用私有目录读取 `quantum-mobile-debug.json`（不超过 4 KiB）。使用已授权的 ADB 和 Debug 包写入配置，然后重启应用：

```powershell
$config = @{ port=9224; token=$env:QUANTUM_MOBILE_DEBUG_TOKEN } | ConvertTo-Json -Compress
$config | adb shell "run-as io.quantum.mobile sh -c 'cat > files/quantum-mobile-debug.json'"
adb shell am force-stop io.quantum.mobile
# 从设备正常启动 Quantum Mobile，或使用开发环境启动。
adb forward tcp:9224 tcp:9224
./scripts/Invoke-MobileDebug.ps1
```

文件位于应用私有存储，无需开放局域网监听，也不通过可被其他应用构造的 Intent 参数配置调试凭据。完成后删除应用私有调试配置并执行 `adb forward --remove tcp:9224`。Release 即使保留该配置也不会读取它或启动接口。Android 端口转发需要设备连接；其运行时验收应在实际 Android 设备/模拟器完成。
