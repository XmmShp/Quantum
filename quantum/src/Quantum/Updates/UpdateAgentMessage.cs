namespace Quantum.Updates;

// Shared wire contract with the standalone macOS updater (no plugin access).
public sealed record UpdateAgentMessage(
    bool Completed = false,
    bool Installed = false,
    string? CurrentVersion = null,
    string? TargetVersion = null,
    string? PendingVersion = null,
    int Progress = 0,
    string? Error = null);
