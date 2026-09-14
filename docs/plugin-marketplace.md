# 插件市场

Quantum 桌面端通过独立部署的 Quantum Platform 浏览和安装已审核插件。市场页位于 `/marketplace`，安装仍由本机插件运行时执行完整预检和事务式切换。

## 连接平台

默认平台地址为 `https://quantum.io-vii.com/`。如需连接本地开发环境或私有部署，可以在启动 Quantum 前配置：

```powershell
$env:QUANTUM_PLATFORM_URL = "https://plugins.example.com"
$env:QUANTUM_VERSION = "0.1.0"
```

`QUANTUM_PLATFORM_URL` 仅接受绝对 HTTP 或 HTTPS 地址；无效值会回退到默认平台地址。

## 安装链路

1. 桌面端通过 Quantum Platform JSON-RPC 搜索已发布插件。
2. 平台从已发布版本中返回支持当前 Quantum 的最高语义版本。
3. 桌面端下载 ZIP，并核对响应声明的大小与 SHA-256。
4. 本机运行时重新读取 manifest，验证依赖、入口、迁移和整包兼容性。
5. 用户确认后才更新 `Modules`；文件替换或运行时启动失败时恢复旧文件和旧运行时代。

插件 SQL migration 仍是 forward-only：一旦提交，不会随之后的生命周期失败自动执行降级。

## 客户端与网页边界

Quantum 原生客户端只负责插件的浏览、下载、安装与升级，不提供创建、上传或审核写操作。

客户端 `/marketplace` 将公开市场与“我的测试版本”放在同一页面：

- 使用邮箱和密码登录，访问令牌保存在操作系统安全存储中；
- 查看自己拥有的插件及其版本状态；
- 下载并安装自己的待审核版本进行本机测试；
- 已发布版本仍可下载安装，被拒绝版本不可下载。

Quantum Platform 网页发布台位于平台地址的 `/portal/`：

- 注册开发者账号，创建、编辑或删除插件条目；
- 上传版本 ZIP，并查看待审核、已发布和已拒绝状态；
- Reviewer 或 Admin 在审核工作台发布或拒绝版本；
- SMTP 启用时，注册欢迎和审核结果由 MailKit 发送。

如果未来需要从客户端发起上传或审核，应由一个官方插件调用平台能力实现，而不是扩张 Quantum 客户端的原生职责。

发布上传仍受当前 JSON-RPC Base64 传输限制，后续应迁移为流式对象存储上传。
