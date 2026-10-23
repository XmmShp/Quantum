# Quantum Platform

Quantum Platform 是 Quantum 的统一后端平台。它负责平台级用户身份与权限，并承载插件发布、审核、检索和下载等业务；后续平台能力继续在同一套 Domain/Contract/Application/Host 边界内扩展。账号与市场 Contract 通过 `POST /rpc` 暴露为 JSON-RPC 2.0，移动中转控制面使用独立的 `POST /rpc/mobile`，网页发布与审核台位于 `/portal/`。两个 NOF 服务不可重复映射同一路由，否则 ASP.NET Core 会报告路由歧义。

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

- 邮箱验证码注册、邮箱密码登录、邮箱验证码找回密码、个人资料、角色与管理员删除流程。
- 开发环境通过日志输出验证码邮件，非开发环境强制使用 SMTP，通过 MailKit 发送验证码、欢迎和版本审核结果邮件。
- 插件目录创建、更新、检索与按作者/标签过滤。
- 插件 ZIP 上传、SHA-256 校验、审核、下载计数，并为当前 Quantum 选择最高兼容版本。
- AgentFn 对待审核包执行受限、不可执行代码的自动安全审核；明确通过后自动发布，拒绝或不确定时保守拒绝/转人工。
- 开发者可在客户端认证下载自己的待审核版本进行测试；该测试下载不计入公开下载量。
- 同源网页工作台提供账号注册、插件资料、版本上传和 Reviewer/Admin 审核流程。
- 管理员审计查询与可配置的安全管理员引导。
- PostgreSQL 持久化，使用 NOF `NOFDbContext`、Repository、Application Parts、Initialization Steps 与自动迁移。
- ZIP 路径穿越、重复路径、体积/条目数、manifest、.NET/Web runtime 入口与 `database.migrations` SQL artifact 校验；文件通过同目录临时文件原子写入。

## 本地开发

需要 .NET 10 SDK 和 PostgreSQL。启动前配置本地数据库连接与用于开发的 JWT 签名密钥：

```bash
export ConnectionStrings__postgres='Host=localhost;Port=5432;Database=quantum_platform;Username=postgres;Password=change-me'
export QuantumPlatform__Jwt__SigningKey='replace-with-a-random-secret-of-at-least-32-characters'
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/Quantum.Platform/Quantum.Platform.csproj
```

以上配置仅用于本地开发。Development 环境的验证码从应用日志读取。
浏览器打开服务根地址进入产品页，开发者发布与审核工作台位于 `/portal/`。

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

## 验证

```bash
dotnet build Quantum.Platform.slnx
dotnet test tests/Quantum.Platform.Tests/Quantum.Platform.Tests.csproj
```

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

插件发布的 CLI 使用指南位于 `/skills/publish-quantum-plugin/manifest.json`。

指定 `--wait-seconds` 后，CLI 等待版本达到 `Published` 才返回成功；拒绝、转人工、审核失败及超时均返回非零退出码。
`--output subprocess` 返回包含状态、版本和 SHA-256 的 JSON。同版本重试要求包哈希与兼容范围保持一致；
修改包内容后需要递增版本号。

## 许可证

项目采用 MIT 许可证，详见 [LICENSE](LICENSE)。
