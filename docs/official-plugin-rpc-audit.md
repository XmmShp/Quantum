# 官方插件 RPC 检查（2026-10-19）

本次检查覆盖本仓库及 Quantum 公开插件目录中的三个已发布官方插件：Calendar、Codex、Ledger。

| 插件 | 发现结果 | 处理 |
| --- | --- | --- |
| Calendar 1.1.1 | 已有 `calendar` NOF 合约、生成服务及全部 7 个处理器注册，覆盖业务应用服务全部操作 | 新增宿主目录及真实 SQLite 调用回归测试；没有缺失接口，不重复发布相同功能 |
| Codex 0.1.1 | 只有内部 `ICodexIntegrationService`，会话状态、模型、连接、新会话、发送、断开没有向 Quantum 暴露 | 0.1.2 新增 `codex` NOF 合约、6 个处理器、描述及 Schema，沿用同一应用服务 |
| Ledger 0.1.1 | 已在前次修复中补齐 8 个 RPC 接口 | 保留并重新运行现有宿主发现与调用测试 |

## Codex 修复中的边界

- 对外暴露 `snapshot`、`models`、`connect`、`new-thread`、`send`、`disconnect`；完整调用名前缀为 `quantum.plugin.codex.codex.`。
- 向本插件拥有的 Codex 会话注入工具时，排除 `quantum.plugin.codex.*`，避免模型递归发送、替换或断开自己的执行会话。其他 Quantum 调用方仍能从宿主目录发现并调用这些接口。
- 将连接失败的 UI 状态转换为明确的 RPC 失败 Result；取消不会被连接逻辑吞掉。
- 只读状态和模型查询不会启动 App Server；连接、发送、新会话和断开等副作用在方法描述与 README 中写明。没有增加自动外发消息或模型请求。
- 保留工作区中已有的 Codex 0.1.1 UTF-8 标准输入输出修复，避免升级回退现有中文消息支持。

## 验证

- 完整 Release 构建，NOF 源生成器启用，零警告、零错误。
- 宿主 RPC 目录、JSON 序列化和调度使用固定来源的真实宿主实现（见集成测试 `HostRpc/provenance.json`）。
- Calendar：发现全部 7 个方法，并以真实 SQLite 和发布迁移执行新增、列表、待办筛选、读取、更新、完成状态和删除；覆盖参数失败、缺失记录和取消。
- Codex：发现全部 6 个方法。真实生产应用服务验证初始状态、模型、断开及失败路径；可控替身验证全部命令的参数、共享状态、业务失败及取消，避免测试启动本机 Codex 或消耗模型用量。
- Ledger：保留金额、CSV、数据库及全部 8 个 RPC 方法的测试。
- Codex 工具隔离回归：自身控制 RPC 被排除，Calendar/Ledger 工具继续保留。

运行两个测试项目：

```sh
dotnet test tests/Quantum.LedgerPlugin.Tests/Quantum.LedgerPlugin.Tests.csproj -c Release
dotnet test tests/Quantum.OfficialPlugins.Tests/Quantum.OfficialPlugins.Tests.csproj -c Release
```

首个测试项目目前同时覆盖三种官方插件的宿主 RPC 集成测试。Windows 本机按 `AGENTS.md` 要求使用管理员进程运行 `dotnet`。

后续新增业务能力时，继续按 [开发指南](plugin-development.md) 同时补齐 NOF 合约、生成元数据、处理器注册、Schema 描述和宿主调用测试。
