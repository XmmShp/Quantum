# 官方插件移动端开发预览

本轮适配基于已发布的 `Quantum.Plugin.Abstraction` SDK `0.1.1-nightly.20261020.26`。页面由桌面插件声明，手机只渲染内置原生控件，点击通过同账号桌面执行现有 NOF RPC。手机不加载插件 DLL，也不包含各插件的数据库或业务实现。

## 接入与使用

1. 使用 包含自动同步的 Quantum 桌面 0.1.12 或更新构建，以及 Quantum Mobile 0.1.4 开发预览客户端（Android APK 或 Windows 便携包）。iOS 尚未完成实机验收。
2. 安装 Calendar 1.3.3、Ledger 0.4.2、Codex 0.1.7 或更新版本；发布与审核状态以插件市场为准。
3. 桌面与手机客户端登录同一 Quantum Platform 账号。桌面自动共享所有可用移动页面，无需勾选或额外批准。桌面必须保持在线，平台必须支持移动控制面。
4. 在手机选择电脑和插件页面。Codex 必须先在桌面完成登录、连接和工作目录/模型选择。

桌面启动插件时发布初始页面，随后每 2 秒通过新的 NOF RPC 作用域读取业务状态并更新声明。宿主对相同声明去重，只把变化推送给正在浏览该页的手机；无需“准备最新数据”或手动重开页面。插件停止时等待同步任务退出并移除页面。日历、记账更新与 Codex 运行进度都会自动同步；读取不会自动连接 Codex 或开始推理。手机保留正在编辑的输入，已成功提交的表单会在新快照中重置。

## 首轮覆盖

| 插件 | 页面 ID | 可用流程 | 当前范围 |
| --- | --- | --- | --- |
| Calendar | `calendar` | 今日事项、未完成待办；添加今日待办；标记待办完成 | 最多 20 条今日事项及 15 条其它日期的未完成待办；快速待办使用页面显示的电脑日期和 12:00 截止时间 |
| Ledger | `ledger` | 本月收入/支出/结余、最近记录；金额/分类/备注与收入开关快速记账 | 最近 15 笔；CNY；保存时采用电脑本地日期；暂不提供移动端关联编辑、历史月份切换或删除 |
| Codex | `codex` | 当前连接/模型/会话、最近消息；继续发送提示词 | 最近 10 条消息，每条长文本截断显示；不提供移动端启动连接、工作目录切换或模型切换；执行中禁止继续发送 |

界面文字使用桌面当前 UI 语言，支持中文和英文；准备最新数据时重新生成。业务失败沿用英文 NOF Result，现有移动客户端的执行结果区仍以原始结果显示。此处是开发预览，不能视为完整的移动版桌面 UI。

## RPC 扩展

所有方法同时进入现有 NOF 目录，沿用宿主的错误/取消管线，无额外执行协议。完整名称为 `quantum.plugin.<plugin>.<service>.<method>`。

| 能力 | 请求 | 行为 |
| --- | --- | --- |
| `calendar.mobile-refresh` | `{}` | 用标准 calendar 查询 Client 重建页面，返回非泛型 Result |
| `ledger.mobile-refresh` | `{}` | 用标准 ledger 查询 Client 重建页面，返回非泛型 Result |
| `codex.mobile-refresh` | `{}` | 只读取 snapshot 并重建页面；不自动连接、不开始推理，返回非泛型 Result |
| `ledger.mobile-create` | `{ "amountText": "12.50", "category": "Food", "note": "Lunch", "isIncome": false }` | 将移动表单的文本金额转换为十进制，再交给标准 create Client；返回普通 LedgerEntryDetails |

`amountText` 必填，使用小数点，不接受千分位、货币符号或指数写法。金额的正数、上限及小数位规则由已有记账领域校验。`category` 必填；`note` 默认为空；`isIncome` 默认为 false。Calendar 的创建和完成按钮直接使用现有 `calendar.create`、`calendar.set-completed`；Codex 发送直接使用现有 `codex.send`。发布页面本身不会执行这些动作。

移动页面与本地化放在各插件的 `UI/Mobile` 和 `UI/Localization`；Application 仅定义发布端口及 RPC 用例，不引用本地化资源。SDK ABI 兼容但未注册移动页面能力的宿主仍可正常启动插件；旧版 SDK ABI 不保证兼容，需升级桌面。主动调用 mobile-refresh 则返回 `mobile_unavailable`。插件停止时移除相应页面，避免旧视图继续操作。

## 验证

集成测试执行 NOF 生成注册与宿主 JSON 分发，验证真实 SQLite 写入、金额失败后的恢复、待办完成、移动页面发布/清理、取消传播、中英文 UI 与英文业务错误。Codex 使用测试运行时验证不自动启动与显式发送，测试不会启动真实模型任务或消耗账户用量。

移动桥接、授权和重投语义见 [Quantum 移动端说明](https://github.com/XmmShp/Quantum/blob/main/mobile/README.md)。本轮插件适配尚需在打包部署后进行 Android/实体设备交互验收。
