# Calendar

Quantum 官方日历插件，提供统一的日程与待办管理。

## 功能

- 月历查看与按日议程
- 日程、待办的创建、编辑和删除
- 待办完成状态、全部/未完成/已完成筛选及逾期提示
- 中英文界面与响应式布局
- 通过 NOF Application Service 和宿主共享 SQLite 持久化
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

## 开发

```powershell
dotnet build .\official-plugins\Calendar\Quantum.CalendarPlugin.csproj
```

构建产物包含 `plugin.json`、SQL migration 和 scoped CSS bundle，可按 Quantum 插件开发文档打包或复制到 `Modules` 目录。
