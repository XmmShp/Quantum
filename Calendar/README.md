# Calendar

Quantum 官方日历插件，提供统一的日程与待办管理。1.2.0 起需要 Quantum 0.1.5 或更新版本，以使用纯白背景和全窗口日历布局。

## 功能

- 月历查看与按日议程
- 日程、待办的创建、编辑和删除；创建和编辑使用独立模态对话框，支持 Esc 关闭和焦点返回，列表布局保持不变
- 待办完成状态、全部/未完成/已完成筛选及逾期提示
- 完成/恢复、编辑、删除使用描边图标；删除首次点击切换为确认按钮，再次点击才删除，失焦、点击空白或 Esc 取消确认
- 中英文界面与响应式布局
- 全窗口月历与右侧当日事项、待办双面板；使用低饱和雾紫配色、无边框日期格及紧凑列表，窄窗口自动重排；月份切换提供不阻塞操作的短时淡入淡出与滑动过渡，并支持减少动态效果设置
- 页面通过生成的 `ICalendarRpcServiceClient` 调用，Handler 直接使用仓储和宿主共享 SQLite 持久化；本地数据使用物理删除，不启用软删除
- 通过 NOF `IRpcService` 向其它 .NET 与 Web 插件提供稳定的跨插件接口

## 插件 RPC

其它插件通过 `IRpcInvoker` 或 Web SDK 的 `context.rpc` 按名称调用，不应解析 Calendar 的私有服务。公开名称为：

- `calendar.list`
- `calendar.list-tasks`
- `calendar.get`
- `calendar.create`
- `calendar.update`
- `calendar.set-completed`
- `calendar.delete`

完整名称统一加插件 id 前缀，例如 `quantum.plugin.calendar.calendar.list-tasks`。请求和响应经过 JSON
序列化边界；未找到事项返回 `calendar_entry_not_found`，校验失败返回 `calendar_invalid_request`。
日期和时间分别使用 ISO `yyyy-MM-dd`、`HH:mm:ss` 字符串，`kind` 使用 `Event` 或 `Task`。

Web 插件调用示例：

```ts
const tasks = await context.rpc.invoke(
  "quantum.plugin.calendar.calendar.list-tasks",
  { includeCompleted: false },
);
```

.NET 插件使用 `IRpcInvoker` 和调用方自有、JSON 结构兼容的 DTO 调用，不需要引用 Calendar 插件程序集：

```csharp
var tasks = await rpc.InvokeAsync<MyCalendarEntry[]>(
    "quantum.plugin.calendar.calendar.list-tasks",
    new { includeCompleted = false },
    Context.Empty,
    cancellationToken);
```

## 内部调用结构

页面使用构造函数注入生成的 `ICalendarRpcServiceClient`，注册到 `LocalCalendarRpcServiceClient`。Handler 承担应用层用例，直接查询仓储、调用领域对象并通过 `IDbContext` 提交；不再使用 `ICalendarApplicationService` / `CalendarApplicationService`。每次本地调用和宿主 RPC 都有独立服务端作用域，页面统一处理失败 Result。

详见 [官方插件开发指南](../docs/plugin-development.md)。

## 开发

```powershell
dotnet build .\official-plugins\Calendar\Quantum.CalendarPlugin.csproj
```

构建产物包含 `plugin.json`、SQL migration 和 scoped CSS bundle，可按 Quantum 插件开发文档打包或复制到 `Modules` 目录。
