# Calendar

Quantum 官方日历插件，提供统一的日程与待办管理。

## 功能

- 月历查看与按日议程
- 日程、待办的创建、编辑和删除
- 待办完成状态、全部/未完成/已完成筛选及逾期提示
- 中英文界面与响应式布局
- 通过 NOF Application Service 和宿主共享 SQLite 持久化

## 开发

```powershell
dotnet build .\official-plugins\Calendar\Quantum.CalendarPlugin.csproj
```

构建产物包含 `plugin.json`、SQL migration 和 scoped CSS bundle，可按 Quantum 插件开发文档打包或复制到 `Modules` 目录。
