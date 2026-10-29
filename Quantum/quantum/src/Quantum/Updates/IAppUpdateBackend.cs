namespace Quantum.Updates;

public interface IAppUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    string? PendingVersion { get; }
    Task<string?> CheckAsync();
    Task DownloadAsync(Action<int> progress);
    Task ApplyAndRestartAsync();
}
