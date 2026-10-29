using Microsoft.Extensions.Logging;

namespace Quantum.Updates;

public enum AppUpdateState { Unavailable, Idle, Checking, Current, Available, Downloading, Ready, Applying, Failed }

/// <summary>One shared update operation for the entire application, independent of page lifetime.</summary>
public sealed class AppUpdateService(IAppUpdateBackend backend, ILogger<AppUpdateService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _startupChecked;
    public event Action? Changed;
    public AppUpdateState State { get; private set; } = backend.IsInstalled
        ? (backend.PendingVersion is null ? AppUpdateState.Idle : AppUpdateState.Ready)
        : AppUpdateState.Unavailable;
    public string CurrentVersion => backend.CurrentVersion;
    public string? TargetVersion { get; private set; } = backend.PendingVersion;
    public int Progress { get; private set; }
    public bool IsBusy => State is AppUpdateState.Checking or AppUpdateState.Downloading or AppUpdateState.Applying;

    public Task CheckOnStartupAsync() => Interlocked.Exchange(ref _startupChecked, 1) == 0
        ? CheckAsync() : Task.CompletedTask;

    public async Task CheckAsync()
    {
        if (!backend.IsInstalled || !await _gate.WaitAsync(0)) return;
        try
        {
            if (backend.PendingVersion is { } pending)
            {
                TargetVersion = pending;
                SetState(AppUpdateState.Ready);
                return;
            }
            SetState(AppUpdateState.Checking);
            TargetVersion = await backend.CheckAsync();
            SetState(backend.PendingVersion is not null ? AppUpdateState.Ready
                : TargetVersion is null ? AppUpdateState.Current : AppUpdateState.Available);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Application update check failed");
            SetState(AppUpdateState.Failed);
        }
        finally { _gate.Release(); }
    }

    // The explicit button click authorizes downloading, closing Quantum and restarting it.
    public async Task UpdateAndRestartAsync()
    {
        if (!backend.IsInstalled || !await _gate.WaitAsync(0)) return;
        try
        {
            if (backend.PendingVersion is null)
            {
                SetState(AppUpdateState.Checking);
                TargetVersion = await backend.CheckAsync();
                if (TargetVersion is null)
                {
                    SetState(AppUpdateState.Current);
                    return;
                }
                Progress = 0;
                SetState(AppUpdateState.Downloading);
                await backend.DownloadAsync(value =>
                {
                    Progress = Math.Clamp(value, 0, 100);
                    Changed?.Invoke();
                });
            }
            TargetVersion = backend.PendingVersion ?? TargetVersion;
            SetState(AppUpdateState.Applying);
            await backend.ApplyAndRestartAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application update failed; the installed application remains available");
            SetState(AppUpdateState.Failed);
        }
        finally { _gate.Release(); }
    }

    private void SetState(AppUpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
