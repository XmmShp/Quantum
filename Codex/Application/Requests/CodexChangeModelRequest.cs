using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexChangeModelRequest(
    [property: Description("当前可用模型 ID；仅更新后续轮次使用的模型，保留当前会话 ID 和消息。运行中拒绝切换。")] string Model);
