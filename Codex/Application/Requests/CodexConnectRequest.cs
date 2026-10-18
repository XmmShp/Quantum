using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexConnectRequest(
    [property: Description("本机已存在的工作目录；空字符串使用 Quantum 当前工作目录。连接会启动本机 Codex App Server 并开放其他插件的 Quantum 工具。")] string WorkingDirectory = "",
    [property: Description("要使用的模型 ID，必须由本机 Codex 支持。默认 gpt-5.6-luna。")] string Model = "gpt-5.6-luna");
