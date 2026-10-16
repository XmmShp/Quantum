# Quantum Platform

Quantum Platform 是 Quantum 的统一后端平台。它负责平台级用户身份与权限，并承载插件发布、审核、检索和下载等业务；后续平台能力继续在同一套 Domain/Contract/Application/Host 边界内扩展。所有业务 Contract 通过 `POST /rpc` 暴露为 JSON-RPC 2.0，网页发布与审核台位于 `/portal/`。

## 目录

```text
src/
├── Quantum.Platform.Domain/          用户、插件、版本与审计领域模型
├── Quantum.Platform.Contract/        强类型 JSON-RPC Contract
├── Quantum.Platform.Application/     NOF RpcServer、Handler 与业务策略
├── Quantum.Platform.UI/              Blazor 页面、组件、交互与前端静态资源
└── Quantum.Platform/                 Host、JWT、EF Core、密码哈希与文件存储
tests/
└── Quantum.Platform.Tests/           领域、版本约束和 ZIP 安全测试
```

## 能力

- 邮箱验证码注册、邮箱密码登录、邮箱验证码找回密码、个人资料、角色与管理员删除流程；首位完成邮箱验证的注册用户自动成为系统管理员。
- 开发环境通过日志输出验证码邮件，非开发环境强制使用 SMTP，通过 MailKit 发送验证码、欢迎和版本审核结果邮件。
- 插件目录创建、更新、检索与按作者/标签过滤。
- 插件 ZIP 上传、SHA-256 校验、审核、下载计数，并为当前 Quantum 选择最高兼容版本。
- AgentFn 对待审核包执行受限、不可执行代码的自动安全审核；明确通过后自动发布，拒绝或不确定时保守拒绝/转人工。
- 开发者可在客户端认证下载自己的待审核版本进行测试；该测试下载不计入公开下载量。
- 同源网页工作台提供账号注册、插件资料、版本上传和 Reviewer/Admin 审核流程。
- 管理员审计查询与可配置的安全管理员引导。
- PostgreSQL 持久化，使用 NOF `NOFDbContext`、Repository、Application Parts、Initialization Steps 与自动迁移。
- ZIP 路径穿越、重复路径、体积/条目数、manifest、.NET/Web runtime 入口与 `database.migrations` SQL artifact 校验；文件通过同目录临时文件原子写入。

## 启动

至少提供数据库连接与长度不少于 32 字符的 JWT 密钥：

```bash
export ConnectionStrings__postgres='Host=localhost;Port=5432;Database=quantum_platform;Username=postgres;Password=change-me'
export QuantumPlatform__Jwt__SigningKey='replace-with-a-random-secret-of-at-least-32-characters'
dotnet run --project src/Quantum.Platform/Quantum.Platform.csproj
```

仓库不包含默认管理员、调试 Token 接口或签名密钥。空数据库中的首位注册用户必须完成邮箱验证，并自动获得
User、Developer、Reviewer、Admin 角色。验证码只保存 PBKDF2 哈希，10 分钟有效，60 秒内不可重发，连续
失败 5 次后必须重新申请。

主要配置：

| 配置 | 环境变量 | 说明 |
| --- | --- | --- |
| `ConnectionStrings:postgres` | `ConnectionStrings__postgres` | PostgreSQL 连接串 |
| `QuantumPlatform:Jwt:SigningKey` | `QuantumPlatform__Jwt__SigningKey` | JWT HMAC 密钥，至少 32 字符 |
| `QuantumPlatform:Storage:BasePath` | `QuantumPlatform__Storage__BasePath` | ZIP 存储根目录，默认 `Files` |
| `QuantumPlatform:Storage:MaxArchiveBytes` | `QuantumPlatform__Storage__MaxArchiveBytes` | 压缩包大小上限 |
| `QuantumPlatform:Storage:MaxExpandedBytes` | `QuantumPlatform__Storage__MaxExpandedBytes` | 解压后声明大小上限 |
| `QuantumPlatform:AutomatedReview:Enabled` | `QUANTUM_PLATFORM_AUTOMATED_REVIEW_ENABLED` | 是否启用 AgentFn 自动审核发布 Worker |
| `QuantumPlatform:AutomatedReview:PrivateJwksPath` | `QuantumPlatform__AutomatedReview__PrivateJwksPath` | 容器内私有 JWKS 路径；Compose 通过 `QUANTUM_PLATFORM_AGENTFN_PRIVATE_JWKS_FILE` 挂载 |
| `QuantumPlatform:AutomatedReview:Model` | `QUANTUM_PLATFORM_AGENTFN_MODEL` | 自动审核使用的 AgentFn 模型引用 |
| `QuantumPlatform:Email:ConfigurationPath` | `QuantumPlatform__Email__ConfigurationPath` | 包含 `QuantumPlatform:Email` 的只读 JSON Secret 路径 |
| `QuantumPlatform:Email:Enabled` | `QuantumPlatform__Email__Enabled` | 是否启用平台邮件 |
| `QuantumPlatform:Email:SmtpHost` | `QuantumPlatform__Email__SmtpHost` | SMTP 主机 |
| `QuantumPlatform:Email:SmtpPort` | `QuantumPlatform__Email__SmtpPort` | SMTP 端口，默认 587 |
| `QuantumPlatform:Email:EnableSsl` | `QuantumPlatform__Email__EnableSsl` | 是否在连接时启用 SSL |
| `QuantumPlatform:Email:Username` | `QuantumPlatform__Email__Username` | SMTP 用户名，可选 |
| `QuantumPlatform:Email:Password` | `QuantumPlatform__Email__Password` | SMTP 密码，不应写入配置文件 |
| `QuantumPlatform:Email:FromAddress` | `QuantumPlatform__Email__FromAddress` | 发件邮箱，启用邮件时必填 |

Development 环境始终使用 `DevelopmentLoggingPlatformEmailSender`，验证码会写入应用日志且不连接 SMTP；插件
审核由 Development-only Reviewer 直接通过，不创建 AgentFn Task，也不会向外发送插件内容。
非开发环境必须启用并正确配置 SMTP，否则应用拒绝启动。验证码邮件发送失败时不会保存该验证码；欢迎和审核
通知发送失败只记录警告，不回滚已经提交的业务事务。

浏览器打开服务根地址会进入产品发布页，页面首屏使用 Blazor 静态服务端渲染，其中发布链路演示由 Blazor WebAssembly 提供交互；开发者发布与审核工作台位于 `/portal/`。服务状态位于 `GET /api/status`，健康检查位于 `GET /health/live`。数据库迁移由 NOF 初始化步骤在服务启动时执行。

## JSON-RPC

请求参数直接放在 `params`，不额外嵌套 `request`：

```http
POST /rpc HTTP/1.1
Content-Type: application/json; charset=utf-8

{
  "jsonrpc": "2.0",
  "id": "registration-code-1",
  "method": "RequestRegistrationEmailCode",
  "params": {
    "email": "developer@example.com"
  }
}
```

收到验证码后调用 `RegisterUser`，同时提交 `username`、`email`、`password` 和六位
`verificationCode`。Development 环境从应用日志读取验证码。

忘记密码时，在发布台登录页选择“忘记密码？”，或调用 `RequestPasswordResetEmailCode`（`email`），
再调用 `ResetPassword`（`email`、`verificationCode`、`newPassword`）。新密码须为 12–1024 字符，
重置后返回登录页使用新密码登录。找回验证码与注册验证码隔离，只保存哈希；10 分钟有效，60 秒内
不会重发，累计 5 次错误后必须重新申请，重置成功立即失效。申请接口对未注册邮箱和重发限制返回统一成功响应，
避免暴露账号是否存在。邮件发送失败记录服务端警告，用户可在 60 秒后重试。

登录后在受保护操作中携带 `Authorization: Bearer {accessToken}`。完整样例见 [Quantum.Platform.http](Quantum.Platform.http)。成功响应的 `result` 是 NOF Result envelope；调用方仍需检查 `result.isSuccess`。

## Docker

从仓库根目录构建：

```bash
docker build -t quantum-platform .
```

也可以在本目录准备 `POSTGRES_PASSWORD`、`QUANTUM_PLATFORM_JWT_SIGNING_KEY` 和独立的 `QUANTUM_PLATFORM_OIDC_SIGNING_KEY_ENCRYPTION_KEY` 后运行：

```bash
docker compose up --build
```

该命令默认使用 Development 环境、日志邮件发送器和本地自动通过审核器。生产环境还需加载邮件与 AgentFn Secret：

```bash
docker compose -f compose.yaml -f compose.production.yaml -f compose.agentfn.yaml up --build
```

### AgentFn 自动审核

仓库中的 [`skills/quantum-plugin-release-review`](skills/quantum-plugin-release-review/SKILL.md) 已发布为私有
Skill `quantum-plugin-release-review@1.0.1`。机器身份固定为 Credential Client
`quantum-platform-review`，仅授予创建审核任务和读取结果所需的 `skill:run`、`task:read`，且只允许调用该 Skill；应用使用
`private_key_jwt`，不保存用户 Access Token。

启用前需要在 AgentFn 为该 Credential Client 配置模型供应商 Token。然后将其私有 JWKS 作为只读 Secret
挂载；不要把 Token、JWKS 内容或命令输出写入仓库或 Actions 日志。Compose 可使用独立覆盖文件：

```bash
export QUANTUM_PLATFORM_AGENTFN_PRIVATE_JWKS_FILE='/secure/path/quantum-platform-review.private.jwks.json'
docker compose -f compose.yaml -f compose.agentfn.yaml up --build
```

Production Worker 会为每个待审核版本创建带幂等键的 AgentFn Task。发送给模型的是发布元数据、完整文件清单和受大小
约束的可审查文本文件，不执行包内脚本或二进制。`approve` 自动发布，`reject` 拒绝发布，
`manual_review` 保持 Pending 等待人工；失败最多重试三次。人工审核与机器结果通过并发版本字段互斥，过期的
机器结果不会覆盖人工决定。自动审核默认关闭，凭据和供应商 Token 就绪后再启用。

## 验证

```bash
dotnet build Quantum.Platform.slnx
dotnet test tests/Quantum.Platform.Tests/Quantum.Platform.Tests.csproj
```

## 生产部署

生产环境部署到 `192.168.50.202`，对外地址为 `https://quantum.io-vii.com`。只有推送到 `main`
分支才会触发 `.github/workflows/deploy-production.yml`；Pull Request 的构建测试继续使用 GitHub 托管
Runner，不会接触生产主机。

生产 Runner 必须注册在本仓库，使用标签 `quantum-platform-prod`。目标机使用 `koala` 用户运行 Runner，
并通过用户级 systemd 服务保持在线。取得一小时内有效的仓库 Runner 注册令牌后，在目标机执行：

```bash
export RUNNER_TOKEN='<repository runner registration token>'
bash scripts/install-production-runner.sh
unset RUNNER_TOKEN
```

部署密钥不存入仓库或 GitHub Actions。目标机必须存在以下权限为 `600` 的文件：

- `~/.config/quantum-platform/production.env`：包含 `POSTGRES_PASSWORD`、`QUANTUM_PLATFORM_JWT_SIGNING_KEY` 和 `QUANTUM_PLATFORM_OIDC_SIGNING_KEY_ENCRYPTION_KEY`。
- `~/.config/quantum-platform/quantum-platform-email.json`：从 notification-service 邮件配置派生的 JSON Secret。
- `~/.config/quantum-platform/quantum-platform-review.private.jwks.json`：AgentFn Credential Client 私有 JWKS。

## 插件 CI 发布

平台通过 OAuth 2.0 Client Credentials 与 `private_key_jwt` 支持无人值守发布。开发者先创建一个仅绑定所属插件的 Credential Client，并把只返回一次的私有 JWKS 存入 CI secret；平台只保存公开 JWKS。CI 使用短期 `plugin:publish` 令牌提交版本，后续仍进入自动审核流程。

```sh
dotnet tool install --global Quantum.Platform.Cli
export QUANTUM_CLIENT_ID=example-ci
export QUANTUM_CLIENT_PRIVATE_JWKS="$(<private.jwks.json)"
quantum-cli plugins publish ./dist/plugin \
  --quantum-version-support ">=0.1.0" \
  --release-notes "CI release" \
  --wait-seconds 900 \
  --output subprocess
```

配套 Codex SKILL 发布在 `/skills/publish-quantum-plugin/manifest.json`。CLI 的 NuGet 包由 `cli-v*` tag 触发发布，使用 NuGet Trusted Publishing 从 GitHub OIDC 换取临时密钥；仓库只需配置 `NUGET_USER` variable，并在 NuGet.org 一次性建立匹配仓库、workflow 与 `nuget-production` environment 的 Trusted Publishing policy。

CLI 0.1.1 支持 `--wait-seconds`（0–3600，默认 0）。指定等待时间后，只有平台状态达到 `Published` 才成功；拒绝、转人工、审核失败及超时均返回非零退出码。JSON 输出包含 `status`、`releaseId`、版本和 SHA-256。同版本重跑仅在包哈希与兼容范围相同的情况下复用现有发布。Credential Client 只能读取其 `plugin:publish` 与插件白名单共同授权的非公开审核状态。

`POSTGRES_PASSWORD` 对应既有 `koala-pp-postgresql`
实例中的独立 `quantum_platform` 用户和同名数据库。Compose 仅把宿主健康检查端口绑定到
`127.0.0.1:5080`，应用同时接入 `agentfn-overlay-net` 和 `koala-pp-overlay-net`。公网流量通过
SakuraFRP 的 HTTPS 隧道以 PROXY protocol v2 转发至 Nginx，再由 Nginx 路由到
`quantum-platform:8080`。

## 许可证

项目采用 MIT 许可证，详见 [LICENSE](LICENSE)。
