using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexSendRequest(
    [property: Description("发送给当前本机 Codex 会话的非空提示词。会启动模型推理，并可能产生账号用量及 Quantum 工具副作用。")] string Prompt);
