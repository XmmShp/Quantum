# Quantum Extension Market

Quantum Extension Market 是 Quantum 的插件发布、审核、检索与下载服务。项目基于 .NET 10 和 NOF 重构，使用 Clean Architecture 分层，所有业务 Contract 通过 `POST /rpc` 暴露为 JSON-RPC 2.0。

本目录完整承接了 [XmmShp/Quantum-ExtensionMarket](https://github.com/XmmShp/Quantum-ExtensionMarket) 的实际功能范围，并以其提交 `2c5c4b1fdeaadf75f2057be57594f67467f4de8d` 为迁移基线。旧仓库的 Controller/Service 单体实现没有原样保留；用户、插件、版本、审核、下载、兼容性与审计能力均已映射为 NOF Domain 与 Handler。详细映射见 [UPSTREAM.md](UPSTREAM.md)。

## 目录

```text
src/
├── Quantum.ExtensionMarket.Domain/          用户、插件、版本与审计领域模型
├── Quantum.ExtensionMarket.Contract/        强类型 JSON-RPC Contract
├── Quantum.ExtensionMarket.Application/     NOF RpcServer、Handler 与业务策略
├── Quantum.ExtensionMarket.Infrastructure/  密码哈希与安全文件存储
└── Quantum.ExtensionMarket/                 ASP.NET Core Host、JWT、EF Core 与迁移
tests/
└── Quantum.ExtensionMarket.Tests/           领域、版本约束和 ZIP 安全测试
```

## 能力

- 用户注册、登录、个人资料、角色与管理员删除流程。
- 插件目录创建、更新、检索与按作者/标签过滤。
- 插件 ZIP 上传、SHA-256 校验、审核、下载计数及 Quantum 版本兼容性判断。
- 管理员审计查询与可配置的安全管理员引导。
- PostgreSQL 持久化，使用 NOF `NOFDbContext`、Repository、Application Parts、Initialization Steps 与自动迁移。
- ZIP 路径穿越、重复路径、体积/条目数、manifest 与入口 DLL 校验；文件通过同目录临时文件原子写入。

## 启动

至少提供数据库连接与长度不少于 32 字符的 JWT 密钥：

```bash
export ConnectionStrings__postgres='Host=localhost;Port=5432;Database=quantum_extension_market;Username=postgres;Password=change-me'
export ExtensionMarket__Jwt__SigningKey='replace-with-a-random-secret-of-at-least-32-characters'
dotnet run --project quantum-extension-market/src/Quantum.ExtensionMarket/Quantum.ExtensionMarket.csproj
```

可选的首次管理员只在配置了密码且对应邮箱不存在时创建：

```bash
export ExtensionMarket__BootstrapAdmin__Username='admin'
export ExtensionMarket__BootstrapAdmin__Email='admin@example.com'
export ExtensionMarket__BootstrapAdmin__Password='replace-with-a-strong-bootstrap-password'
```

创建后应移除这三个引导变量。仓库不包含默认管理员、调试 Token 接口或签名密钥。

主要配置：

| 配置 | 环境变量 | 说明 |
| --- | --- | --- |
| `ConnectionStrings:postgres` | `ConnectionStrings__postgres` | PostgreSQL 连接串 |
| `ExtensionMarket:Jwt:SigningKey` | `ExtensionMarket__Jwt__SigningKey` | JWT HMAC 密钥，至少 32 字符 |
| `ExtensionMarket:Storage:BasePath` | `ExtensionMarket__Storage__BasePath` | ZIP 存储根目录，默认 `Files` |
| `ExtensionMarket:Storage:MaxArchiveBytes` | `ExtensionMarket__Storage__MaxArchiveBytes` | 压缩包大小上限 |
| `ExtensionMarket:Storage:MaxExpandedBytes` | `ExtensionMarket__Storage__MaxExpandedBytes` | 解压后声明大小上限 |

健康检查位于 `GET /health/live`。数据库迁移由 NOF 初始化步骤在服务启动时执行。

## JSON-RPC

请求参数直接放在 `params`，不额外嵌套 `request`：

```http
POST /rpc HTTP/1.1
Content-Type: application/json; charset=utf-8

{
  "jsonrpc": "2.0",
  "id": "register-1",
  "method": "RegisterUser",
  "params": {
    "username": "developer",
    "email": "developer@example.com",
    "password": "a-strong-development-password"
  }
}
```

登录后在受保护操作中携带 `Authorization: Bearer {accessToken}`。完整样例见 [Quantum.ExtensionMarket.http](Quantum.ExtensionMarket.http)。成功响应的 `result` 是 NOF Result envelope；调用方仍需检查 `result.isSuccess`。

## Docker

从仓库根目录构建：

```bash
docker build -f quantum-extension-market/Dockerfile -t quantum-extension-market .
```

也可以在本目录准备 `POSTGRES_PASSWORD` 和 `EXTENSION_MARKET_JWT_SIGNING_KEY` 后运行：

```bash
docker compose -f quantum-extension-market/compose.yaml up --build
```

## 验证

```bash
dotnet build quantum-extension-market/src/Quantum.ExtensionMarket/Quantum.ExtensionMarket.csproj
dotnet test quantum-extension-market/tests/Quantum.ExtensionMarket.Tests/Quantum.ExtensionMarket.Tests.csproj
```

## 许可证

上游代码为 MIT，版权声明与许可文本保留在 [LICENSE](LICENSE)。
