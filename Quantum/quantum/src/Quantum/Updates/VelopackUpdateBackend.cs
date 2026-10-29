using Velopack;
using Velopack.Sources;

namespace Quantum.Updates;

public sealed class VelopackUpdateBackend : IAppUpdateBackend
{
    // Public releases require no embedded credentials. Never ship a GitHub PAT.
    public const string RepositoryUrl = "https://github.com/XmmShp/Quantum";
    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false));
    private UpdateInfo? _update;

    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString()
        ?? typeof(VelopackUpdateBackend).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string? PendingVersion => _manager.UpdatePendingRestart?.Version.ToString();

    public async Task<string?> CheckAsync()
    {
        _update = await _manager.CheckForUpdatesAsync();
        return _update?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress) => _manager.DownloadUpdatesAsync(
        _update ?? throw new InvalidOperationException("Check for updates before downloading."), progress);

    public Task ApplyAndRestartAsync()
    {
        _manager.ApplyUpdatesAndRestart(
            _manager.UpdatePendingRestart ?? throw new InvalidOperationException("No prepared update."));
        return Task.CompletedTask;
    }
}
