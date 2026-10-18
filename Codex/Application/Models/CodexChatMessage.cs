namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexChatMessage(
    Guid Id,
    CodexMessageRole Role,
    string Text,
    DateTimeOffset CreatedAt,
    bool IsStreaming = false);
