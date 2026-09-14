# 插件市场

Quantum 桌面端通过独立部署的 Quantum Platform 浏览和安装已审核插件。市场页位于 `/marketplace`，安装仍由本机插件运行时执行完整预检和事务式切换。

## 连接平台

默认平台地址为 `http://localhost:5080/`，与 `quantum-platform/compose.yaml` 暴露的端口一致。可以在启动 Quantum 前配置：

```powershell
$env:QUANTUM_PLATFORM_URL = "https://plugins.example.com"
$env:QUANTUM_VERSION = "0.1.0"
```

`QUANTUM_PLATFORM_URL` 仅接受绝对 HTTP 或 HTTPS 地址；无效值会回退到本地默认地址。生产环境应使用 HTTPS。

## 安装链路

1. 桌面端通过 Quantum Platform JSON-RPC 搜索已发布插件。
2. 平台从已发布版本中返回支持当前 Quantum 的最高语义版本。
3. 桌面端下载 ZIP，并核对响应声明的大小与 SHA-256。
4. 本机运行时重新读取 manifest，验证依赖、入口、迁移和整包兼容性。
5. 用户确认后才更新 `Modules`；文件替换或运行时启动失败时恢复旧文件和旧运行时代。

插件 SQL migration 仍是 forward-only：一旦提交，不会随之后的生命周期失败自动执行降级。

## 当前边界

当前市场页面向匿名浏览、详情、下载、安装与升级。开发者登录、发布管理和审核工作台仍通过 Quantum Platform JSON-RPC 完成，将在后续界面中补齐。
