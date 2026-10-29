# Quantum

Quantum 是本地优先的插件平台。本仓库统一维护桌面/移动客户端、SDK、官方插件和平台后端，并保留四个来源仓库的 Git 历史。

| 目录 | 内容 |
| --- | --- |
| [Quantum](Quantum/README.md) | 桌面宿主、移动伴随客户端、内部协议、插件示例及开发文档 |
| [Quantum.Sdk](Quantum.Sdk/README.md) | .NET / TypeScript 插件 SDK |
| [Quantum.OfficialPlugins](Quantum.OfficialPlugins/README.md) | Calendar、Codex、Ledger 官方插件 |
| [Quantum.Platform](Quantum.Platform/README.md) | 账号、插件市场、审核、移动中转及发布 CLI |
| [ci](ci/README.md) | GitLab Runner 验证、发布、镜像构建与 Runner 要求 |

主仓库：[GitLab io7/quantum](https://git.koala-studio.org.cn/io7/quantum)。
公开镜像：[GitHub XmmShp/Quantum](https://github.com/XmmShp/Quantum)。请在 GitLab 提交代码和 Merge Request；GitHub 不运行 Actions，也不作为上游接受变更。

## 开发

需要 .NET 10 SDK、Node.js 24 和 Python 3。各组件的 NuGet 版本管理保持独立；宿主和官方插件直接引用同仓库 SDK。

```sh
dotnet restore Quantum.Core.slnx -p:QuantumCoreOnly=true
dotnet build Quantum.Core.slnx -c Release -p:QuantumCoreOnly=true --no-restore
dotnet test Quantum.Core.slnx -c Release -p:QuantumCoreOnly=true --no-build
npm ci --prefix Quantum.Sdk/typescript
npm test --prefix Quantum.Sdk/typescript
```

`Quantum.Core.slnx` 包含跨平台核心与测试，不包含原生 MAUI 应用。Windows/macOS 桌面及移动构建见 [Quantum](Quantum/README.md)。Windows 本地运行 `dotnet` 必须使用管理员权限。

## 发布与镜像

所有流水线由根目录 `.gitlab-ci.yml` 驱动。GitLab 自动将分支与标签推送到 GitHub，GitHub 只提供公开源码及已有下载资源。发布标签、凭据及平台 Runner 配置见 [CI/CD 说明](ci/README.md)。

历史标签保留于 `legacy/<原仓库>/<原标签>`；桌面、移动、插件和 CLI 的既有发布标签保持原名，SDK 标签使用 `sdk/vX.Y.Z`。历史标签指向迁移前的提交，新的发布应在 Mono-Repo 提交上打标签。
