namespace Quantum.OfficialPlugins.Codex;

/// <summary>Only a notification signal; consumers read and mutate state through RPC.</summary>
public interface ICodexRuntimeEvents
{
    event Action? Changed;
}
