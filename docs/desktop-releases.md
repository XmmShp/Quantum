# 桌面安装包与应用内更新

Quantum 的直接分发渠道采用 Velopack 1.2.0，更新源为公开的 [GitHub Releases](https://github.com/XmmShp/Quantum/releases)。Windows 安装包为 `Quantum-win-x64-Setup.exe`；macOS 为 Apple Silicon / Intel 分别生成 `.pkg` 安装向导。用户不必安装 .NET SDK 或 MAUI workload。

Windows 打包包含 .NET 与 Windows App SDK；安装器会在缺少 WebView2 或 VC++ Runtime 时联网安装依赖。macOS 包含 MAUI runtime 及独立的 CoreCLR 更新助手。

## 需要准备的清单

在 GitHub 仓库 **Settings → Secrets and variables → Actions** 配置以下项目。证书必须包含私钥；不要上传到仓库，也不要把 GitHub token 嵌入客户端。

| 平台 / 用途 | 类型与名称 | 内容 / 要求 |
| --- | --- | --- |
| GitHub 发布 | 自动提供的 `GITHUB_TOKEN` | workflow 已给最终发布 job `contents: write`；不需要额外 PAT。仓库 / 组织策略必须允许该权限。 |
| Windows 签名 | Secret `WINDOWS_CERTIFICATE_BASE64` | 受信任 CA 颁发、允许导出的 Authenticode 代码签名证书及私钥，PFX 的 Base64。仅持硬件令牌或云托管私钥时，应改接提供商的签名接口，不能直接使用这条 PFX 分支。 |
| Windows 签名 | Secret `WINDOWS_CERTIFICATE_PASSWORD` | PFX 导出密码。 |
| Windows 强制签名 | Variable `REQUIRE_WINDOWS_SIGNING=true` | 建议正式发布启用；缺证书则失败。未设置时可生成未签名包用于试用，系统可能显示安全提示。 |
| macOS 开发者资格 | Apple Developer Program | 同一团队下创建下面两种 Developer ID 证书；不是 Apple Development / App Store Distribution 证书。 |
| macOS 应用签名 | Secret `MACOS_APPLICATION_CERTIFICATE_BASE64` | **Developer ID Application** 证书与私钥导出的 P12，Base64。 |
| macOS 安装器签名 | Secret `MACOS_INSTALLER_CERTIFICATE_BASE64` | **Developer ID Installer** 证书与私钥导出的 P12，Base64。 |
| macOS 证书密码 | Secret `MACOS_CERTIFICATE_PASSWORD` | 两个 P12 使用相同导出密码。 |
| macOS 签名身份 | Secret `MACOS_APPLICATION_IDENTITY` | 如 `Developer ID Application: Your Name`，按 Velopack 要求省略末尾 `(TEAMID)`。 |
| macOS 安装器身份 | Secret `MACOS_INSTALLER_IDENTITY` | 如 `Developer ID Installer: Your Name`，省略末尾 `(TEAMID)`。 |
| Apple 公证 | Secrets `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_SPECIFIC_PASSWORD` | Apple ID 邮箱、Team ID、Apple ID 专用密码。CI 创建临时 keychain / notarytool profile，打包时完成签名及公证，结束后删除 keychain。 |
| 启用 macOS | Variable `ENABLE_MACOS_RELEASE=true` | 配齐 Apple 凭据后启用，加入 `macos-15` 上的 arm64 / x64 构建。未启用时仅发布 Windows。 |

PFX / P12 转 Base64（PowerShell）：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\private\certificate.pfx')) | Set-Clipboard
```

将剪贴板内容直接粘贴到对应 GitHub Secret。Windows 签名默认使用 DigiCert RFC3161 时间戳服务器。签名不保证立即获得 SmartScreen 信誉，详见 [Velopack 签名说明](https://docs.velopack.io/packaging/signing)。

macOS 的直接分发必须关闭 App Sandbox，使用 `DirectDistribution.entitlements`；保留默认沙箱配置供原有本地开发使用。该发行路线不适用于 Mac App Store。现有沙箱构建的数据位于容器中，首次切换至直接分发不会自动搬迁旧容器数据；需要在旧应用导出插件 / 数据后迁移。详见 [macOS 分发限制](https://docs.velopack.io/packaging/operating-systems/macos)。

## 发布步骤

1. 先推送代码，在 **Actions → Release Desktop → Run workflow** 输入 `0.2.0`，生成 CI artifacts 做安装验收；手动运行不会创建 Release，也不会向客户端投放更新。
2. 在干净 Windows 测试机安装 Setup.exe，确认首次启动、WebView、插件、语言切换；启用 macOS 时也在 arm64 / Intel 机器安装对应 `.pkg`。
3. 在要发布的提交创建并推送正式版本标签：

   ```bash
   git tag desktop-v0.2.0
   git push origin desktop-v0.2.0
   ```

4. 下一次使用更大的 `X.Y.Z`。workflow 从最近包含对应 channel feed 的已发布正式 Release 下载完整基线与历史增量链，生成 full / delta 包，然后等待所有启用的平台成功。
5. 发布 job 创建草稿，上传完整安装包、更新包、`releases.<channel>.json` 与 SHA256 清单，再公开 Release。任一构建失败不会创建公开 Release。只在确实没有历史基线时允许首发 full-only；API / 下载错误会让构建失败。

标签仅接受 `desktop-vX.Y.Z`，不支持预发布通道。`ApplicationDisplayVersion` / assembly / Velopack 包版本均来自标签；Apple build number 使用 Actions run number。不要复用已发布版本或修改既有安装包。若上传中途失败，检查并删除该次未公开草稿后再重跑；已公开 Release 不应被覆盖。

保留历史 full / delta 及 feed，避免旧客户端无法跨版本更新。仓库需公开才能让无凭据客户端读取 Releases；如要私有分发，应接入受认证更新服务，不能把 PAT 内置到客户端。当前每个发行平台使用独立的 `win-x64` / `osx-arm64` / `osx-x64` channel，客户端从安装元数据读取 channel。

## 应用内行为

- 启动后后台检查一次正式版本，有更新时显示跳转设置的提示；不会自动下载或关闭应用。
- 设置页显示当前版本、目标版本、状态和下载进度。“一键更新并重启”会重新检查、下载、校验、应用更新并重启。点击前应保存工作。
- Velopack 优先使用增量链；缺基线、增量过多、增量损坏或合成失败时回退完整下载。首发没有上一版本，因此只有完整包。
- 断网 / 下载失败显示可重试状态，不替换当前安装；已准备好的更新会在下次检查时恢复，可直接安装。
- 开发目录或非 Velopack 安装禁用更新。Windows / macOS 用户数据、数据库与插件使用现有应用数据目录，不打进更新包；显式配置插件到应用安装目录的用户应迁至数据目录，防止更新替换包内容。

Windows 在自定义 `Main` 中先运行 Velopack，再启动 WinUI。Mac Catalyst 在 .NET 中标识为 iOS / Catalyst，不能直接调用 Velopack 的 macOS 托管 SDK；因此 `Quantum.UpdateAgent` 是捆绑在 `.app/Contents/Helpers` 内的自包含 macOS 控制台程序。Catalyst 通过 `posix_spawn` 启动它，用私有缓存中的随机 JSON 响应文件交换状态与进度，不经过 shell。它调用同一 Velopack API，并在应用退出后交给 `UpdateMac` 替换 / 重启 bundle。

更新信任边界是公开 GitHub Release、HTTPS 以及发布凭据；客户端由 Velopack 校验更新包 checksum。仓库的写入权限和 Actions secrets 应只授予发布维护者。协议没有另外引入自有离线更新签名密钥。

## 本地验证

在 Windows 上以管理员权限运行所有包含 `dotnet` 的命令：

```powershell
dotnet tool restore
dotnet test quantum/tests/Quantum.Tests/Quantum.Tests.csproj -c Release
./scripts/Build-DesktopRelease.ps1 -Version 0.2.0 -Channel win-x64
```

第二次构建更高版本时保留 `artifacts/releases/win-x64`，即可验证增量生成。若重做同一版本，应使用新的独立输出工作区；脚本拒绝与现有基线相同或更低的版本。

实际安装验收需至少发布两个版本：先安装旧版并写入示例插件 / 设置 / 数据，再发布新版，在旧版设置页更新并检查重启后版本与数据。另测断网重试、跳过一个版本、增量损坏回退及 macOS `/Applications` 权限提示。macOS 编译、签名、公证和 GUI 更新必须在配有 Apple 凭据的 macOS runner / 测试机上验收；Windows 本机无法替代这些验证。

参考：[Velopack GitHub Actions](https://docs.velopack.io/distributing/github-actions)、[UpdateManager API](https://docs.velopack.io/reference/cs/Velopack/UpdateManager)、[Apple 公证](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)。
