# 移动 UI 开发预览

本功能以首版开发预览发布，协议版本为 `schemaVersion: 1`。使用同一发布版本的 NuGet 包 `Quantum.Plugin.Abstraction` 与 npm 包 `@io7/quantum-plugin-sdk`；main 推送产生 nightly，npm 使用 `nightly` 标签。桌面、平台和手机需使用兼容的协议版本。移动通信由框架内部模块共享，不作为插件 SDK 或独立 NuGet 包发布。

手机只接收版本化控件树和不透明动作 ID。界面声明、插件代码和 RPC 参数组装均在桌面。已支持 VStack、HStack、Scroll、Text、TextField、Toggle、Button、Divider、Spacer 和 Progress。

从上一预览升级时，将 `Quantum.Mobile.Protocol.Mobile` 改为 `Quantum.Plugin.Abstraction.Mobile`，并移除对 `Quantum.Mobile.Protocol` 的引用。页面 DTO 使用必填成员与属性初始化；ActionId、InputIds、设备注册和通信客户端属于框架内部，不是插件 API。

## .NET

在插件 StartAsync 从 DI 取得 `IQuantumMobilePages`，发布页面：

```csharp
using Quantum.Plugin.Abstraction;
using Ui = Quantum.Plugin.Abstraction.Mobile;

services.GetRequiredService<IQuantumMobilePages>().Publish(
    Ui.Page("quick-note", "Quick note", Ui.VStack("root",
        Ui.TextField("content").Hint("Content").Limit(500),
        Ui.Button("save", "Save").OnClick(
            Ui.Rpc("quantum.plugin.notes.notes.create")
              .Constant("notebookId", "inbox")
              .Input("text", "content")))));
```

页面包含当下的展示数据，不包含移动执行的委托。再次 Publish 同一 pageId 替换页面并使旧动作失效；StopAsync 调用 Remove。插件也可在本地业务状态改变时重新发布。用户在手机点击刷新获得新版本，当前没有桌面数据变更自动刷新正在编辑的页面。

## TypeScript

```typescript
import { definePlugin, Mobile, validateMobilePage } from "@io7/quantum-plugin-sdk";

export default definePlugin({
  async activate(context) {
    const page = Mobile.page("quick-note", "Quick note", Mobile.vStack("root",
      Mobile.textField("content").hint("Content").limit(500),
      Mobile.button("save", "Save").onClick(
        Mobile.rpc("quantum.plugin.notes.notes.create")
          .constant("notebookId", "inbox").input("text", "content"))));
    validateMobilePage(page);
    await context.mobile.publish(page);
    return () => context.mobile.remove(page.id);
  }
});
```

TS 的 publish/remove 是实际 iframe→Host capability，不是仅类型声明。Host 验证当前插件与 runtime 所有权，并在 iframe runtime 释放时清理页面。.NET 发布是进程内同步操作；TS 发布因跨 iframe 消息边界为 Promise，这是传输方式导致的明确差异。两者经过同一 Host 校验器，错误分别为异常与 rejected Promise；不发起业务 RPC，所以没有在 Publish 中取消已执行业务的语义。

## 动作规则

- RPC 必须是完整 `pluginId.service.method`，不接受短名或 Alias；当前目标需已启动。按钮绑定在打开手机页面时固定到目标运行代。
- 常量与输入字段不能重叠；Input 的 source 必须是页面内 TextField/Toggle ID。首版不支持嵌套路径、模板表达式、脚本或动态方法名。
- .NET Constant 接受 JSON 基本值或 JsonElement；结构化常量先由插件自己的序列化器转成 JsonElement，避免协议层持有插件私有 CLR 类型。TS 常量直接使用等价 JSON 数据。
- 控件 ID、页面 ID 和参数字段使用 1–128 个字母、数字、点、下划线、连字符，首字符是字母或数字。
- 单页上限 128 KiB、500 节点、16 层；字符串最长 2,000 字符，动作输入最多 16 KiB。
- 业务仍由现有 NOF Handler 校验。客户端验证、页面共享和设备授权不代替业务规则。
- `quantum.mobile.*` 为 Host 保留 Context 前缀；设备、requestId、viewId 由网关注入，.NET/TS 普通 RPC 调用者不能伪造。
- 同一动作请求 ID 的网络重投不会再次执行；进入 RPC 后结果未知时不会自动重做。业务需要更强保证时必须自行按 requestId 实现幂等。

## 模拟器

`Quantum/mobile/Quantum.Mobile` 的 Windows/Mac Catalyst 目标与手机使用同一个原生 Renderer。打开应用后可选 Mock 示例；桌面目标还可用“打开 UI JSON”加载本 SDK 的 `fixtures/mobile-page-v1.json`。文件里的动作会替换为 Mock ID，只展示输入，不调用业务 RPC。登录平台后则可以作为真实手机设备联调桌面共享。

手机应用只引用框架内部的通信模块；该模块不公开发包，也不依赖插件 SDK、NOF 或桌面 Host。新增组件需要更新协议校验器、两种 builder、Host 和内置 Renderer，再发布移动应用版本。

## 首版范围与兼容性

首版覆盖页面发布/替换/移除、固定控件树、不透明动作 ID、声明式参数绑定、设备授权、消息租约与 Ack、请求去重、结果查询及插件运行代失效。平台传输由桌面和手机负责；插件不能选择远程 RPC 名称或读取设备凭据。

本版本尚不包含按会话异步页面 provider、自动刷新推送、动态语言重建、完整显示能力协商和 iOS 实机验收。页面使用显式刷新。断线恢复可能返回 unknown，不保证 exactly-once，也不自动重做结果未知的业务操作。新增控件或变更 schema 需要同时升级校验器与移动 Renderer。协议版本不支持时应拒绝页面，不能忽略必要控件后继续调用动作。
