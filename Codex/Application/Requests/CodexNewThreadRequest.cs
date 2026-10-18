using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexNewThreadRequest(
    [property: Description("可选模型 ID；省略时保留当前模型。此操作会清空当前显示的会话消息。")] string? Model = null);
