# Quantum TypeScript 插件开发

Quantum Web 插件不需要 DLL。宿主把每代插件复制到影子目录，在一个 `sandbox="allow-scripts"`、
opaque-origin 的独立 iframe 中执行入口模块；卸载或热更新时销毁整个 iframe。

## 1. 项目结构

```text
MyPlugin/
├── migrations/
│   └── 001_init.sql
├── package.json
├── plugin.json
├── src/
│   └── index.ts
└── wwwroot/
    └── dist/
        └── plugin.js
```

安装 SDK 和 bundler：

```bash
npm install @quantum/plugin-sdk
npm install --save-dev typescript esbuild
```

入口必须是单文件 ESM bundle。iframe 不允许运行时导入 npm 包，因此所有依赖必须打进入口：

```json
{
  "scripts": {
    "build": "esbuild src/index.ts --bundle --format=esm --platform=browser --target=es2022 --outfile=wwwroot/dist/plugin.js"
  }
}
```

## 2. Manifest

```json
{
  "id": "quantum.plugin.notes",
  "version": "1.0.0",
  "runtime": {
    "kind": "web",
    "entry": "dist/plugin.js"
  },
  "database": {
    "migrations": "./migrations"
  },
  "integrations": [],
  "ui": {
    "routes": [{
      "path": "/plugins/notes",
      "view": "main",
      "title": "Notes",
      "icon": "N"
    }]
  }
}
```

`runtime.entry` 相对于 `wwwroot`，必须以 `.js` 或 `.mjs` 结尾，不能包含反斜杠、冒号、绝对路径或 `..`。
Web 插件不能使用 `web.head`/`web.postBlazor` 向宿主页面注入 HTML；样式和页面只能写入自己的 iframe。

`database.migrations` 是 Host 能力，不是 JavaScript API。它与 .NET 插件使用完全相同的发布 artifact：把 Prisma、
Drizzle 或其他开发期 ORM 的最终升级路径导出为 SQLite SQL，按 `001_init.sql`、`002_add_index.sql` 的形式放入声明目录。
每个版本携带完整的追加式历史；Host 在 `activate` 之前用事务执行待应用文件，并校验已应用文件的 SHA-256。
Web iframe 不会获得数据库连接；需要读写业务数据时仍应通过 Host capability 或声明的 .NET integration RPC。

完整的命名、事务和 forward-only 升级规则见 [.NET 插件开发指南的持久化章节](plugin-development.md#6-提供静态资源和-web-贡献)。

## 3. 生命周期与页面

```ts
import { definePlugin } from "@quantum/plugin-sdk";

export default definePlugin({
  async activate(context) {
    await context.log.info("started");
    return () => context.log.info("stopped");
  },

  async mount({ element, route, signal }) {
    element.textContent = route.view;
    signal.addEventListener("abort", () => element.replaceChildren(), { once: true });
    return () => element.replaceChildren();
  }
});
```

- `activate` 每代 runtime 调用一次；其返回的 cleanup 在 `deactivate` 前调用。
- `mount` 在路由展示时调用；切走路由时先 abort `signal`，再调用 cleanup 和 `unmount`。
- `context.signal` 在整个 runtime 停止时 abort。
- 即使插件没有正确清理，宿主最终也会删除 iframe，浏览上下文中的 DOM、定时器和事件随之释放。

## 4. 宿主能力

SDK 提供日志、导航、环境快照、Topic EventBus、资源读取和通用 RPC：

```ts
const environment = await context.environment.snapshot();
await context.navigation.navigate("/");
const text = await context.assets.readText("data/default.json", { signal });
const imageUrl = context.assets.url("images/icon.png");
```

iframe 的 CSP 禁止直接网络连接。文本资源可通过 `assets.readText` 读取，单次上限 2 MiB；图片、字体和样式可以使用
`assets.url()` 返回的宿主 URL。

## 5. 使用 Topic EventBus

Web 插件与 .NET 插件共享同一套 Host EventBus。Topic 必须通过 branded factory 创建，校验规则与 .NET 的 NOF
`QuantumTopic` 值对象一致：最大长度 255，并且必须匹配
`^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$`。

```ts
import { QuantumTopic } from "@quantum/plugin-sdk";

const topic = QuantumTopic.of("devices.status");
const subscription = await context.eventBus.subscribe(topic, async event => {
  const payload = event.payload as { deviceId?: string; state?: string };
  await context.log.info(
    `${event.publisher.id} published ${payload.state ?? "unknown"} to ${event.topic}`);
});

const publisher = context.eventBus.createPublisher<{
  deviceId: string;
  state: string;
}>(topic);
await publisher.publish({ deviceId: "camera-1", state: "ready" });

// 在 activate cleanup 或不再需要监听时释放。
await subscription.dispose();
```

`QuantumEvent.payload` 是从 Host JSON envelope 得到的原始 JavaScript 值，类型为 `unknown`；handler 应用类型守卫、
schema validator 或显式收窄后再使用。发布会等待当前所有 .NET 与 Web handler 完成，Web handler 抛错也会返回发布端。
消息不持久化、不重放。runtime deactivate 会自动退订，iframe 非正常销毁时 Host 也会按 `pluginId + runtimeId` 释放全部
订阅，防止旧运行代继续收到消息。

## 6. 调用 .NET 服务

```ts
const result = await context.dotnet.invoke<MyResult>({
  target: "host",
  service: "My.Contracts.INotesService",
  method: "FindAsync",
  arguments: [{ text: "quantum" }],
  parameterTypes: ["My.Contracts.NoteQuery"]
}, { signal });
```

`target` 可以是 `host`，也可以是 manifest 中已激活的 .NET integration 插件 id；后者从目标插件的私有容器解析服务。
Quantum 将已安装插件视为受控代码，导航和 .NET 服务调用均可直接使用。

互操作约束：

- 服务名和 `parameterTypes` 使用不带程序集名与 `global::` 的 `Type.FullName`。
- 只允许调用服务契约上的 public instance method；不允许泛型方法、指针或 `ref`/`out` 参数。
- 参数和结果必须可由 `System.Text.Json` 序列化；重载无法唯一匹配时必须填写 `parameterTypes`。
- `CancellationToken` 参数由宿主注入。JS Abort 会请求取消，宿主同时有 30 秒超时。
- 每次调用创建独立 DI scope；返回值序列化完成后释放 scope，不能返回需要继续使用的 scoped 对象或句柄。
  如果异步方法忽略取消，RPC 会按时结束，但宿主会把 scope 保留到实际任务结束，避免提前释放 scoped 服务。

## 7. 构建与调试

```bash
npm run build
```

把 `plugin.json` 和 `wwwroot` 复制到 `Modules/<plugin-id>` 后执行“重新扫描 Modules”。仓库内的
`samples/Quantum.ExampleWebPlugin` 展示了 iframe 页面、环境查询、导航，以及与
`samples/Quantum.ExamplePlugin` 的双向 integration 声明和 .NET FQN 异步握手调用；打开 .NET 示例页可以看到
JS 发起的累计握手次数。

当前自动化环境验证 manifest、运行时、市场包、TypeScript 类型和 bundle 语法。Windows WebView2 与 Mac Catalyst
WKWebView 仍应各做一次实机验证，尤其关注 sandbox iframe 加载和 `postMessage` 行为。

Web runtime 的 .NET 快照切换和 iframe 激活分属两个异步阶段：新 iframe 激活失败时宿主会报告错误并拒绝旧
runtime 的后续 RPC，但当前版本还不能把 .NET 侧已经提交的插件快照自动回滚。入口 bundle 应尽量把可失败的初始化放在
`activate`，并在发布前同时验证 WebView2 与 WKWebView。
