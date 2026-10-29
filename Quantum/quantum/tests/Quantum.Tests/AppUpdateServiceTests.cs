using Microsoft.Extensions.Logging.Abstractions;
using Quantum.Updates;

namespace Quantum.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task DevelopmentBuildNeverChecksOrApplies()
    {
        var backend = new FakeBackend { IsInstalled = false };
        var service = Create(backend);
        await service.CheckOnStartupAsync();
        await service.UpdateAndRestartAsync();
        Assert.Equal(AppUpdateState.Unavailable, service.State);
        Assert.Equal(0, backend.CheckCount);
        Assert.False(backend.Applied);
    }

    [Fact]
    public async Task StartupCheckRunsOnlyOnceAndNeverDownloads()
    {
        var backend = new FakeBackend();
        var service = Create(backend);
        await service.CheckOnStartupAsync();
        await service.CheckOnStartupAsync();
        Assert.Equal(1, backend.CheckCount);
        Assert.Equal(AppUpdateState.Available, service.State);
        Assert.False(backend.Downloaded);
        Assert.False(backend.Applied);
    }

    [Fact]
    public async Task OneClickDownloadsBeforeApplyingAndReportsProgress()
    {
        var backend = new FakeBackend();
        var service = Create(backend);
        await service.UpdateAndRestartAsync();
        Assert.True(backend.Downloaded);
        Assert.True(backend.Applied);
        Assert.Equal(100, service.Progress);
        Assert.Equal("0.2.0", service.TargetVersion);
    }

    [Fact]
    public async Task FailedDownloadNeverAppliesAndCanBeRetried()
    {
        var backend = new FakeBackend { FailDownload = true };
        var service = Create(backend);
        await service.UpdateAndRestartAsync();
        Assert.Equal(AppUpdateState.Failed, service.State);
        Assert.False(backend.Applied);
        backend.FailDownload = false;
        await service.UpdateAndRestartAsync();
        Assert.True(backend.Applied);
    }

    [Fact]
    public async Task PreparedUpdateSurvivesRestartWithoutRedownload()
    {
        var backend = new FakeBackend { PendingVersion = "0.2.0" };
        var service = Create(backend);
        Assert.Equal(AppUpdateState.Ready, service.State);
        await service.CheckAsync();
        await service.UpdateAndRestartAsync();
        Assert.Equal(0, backend.CheckCount);
        Assert.False(backend.Downloaded);
        Assert.True(backend.Applied);
    }

    [Fact]
    public async Task ConcurrentClicksDoNotStartAnotherOperation()
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend { CheckResult = completion.Task };
        var service = Create(backend);
        var check = service.CheckAsync();
        await service.CheckAsync();
        await service.UpdateAndRestartAsync();
        Assert.Equal(1, backend.CheckCount);
        Assert.False(backend.Applied);
        completion.SetResult(null);
        await check;
        Assert.Equal(AppUpdateState.Current, service.State);
    }

    [Fact]
    public async Task FailedCheckReleasesLockForRetry()
    {
        var backend = new FakeBackend { CheckResult = Task.FromException<string?>(new IOException("Offline")) };
        var service = Create(backend);
        await service.CheckAsync();
        Assert.Equal(AppUpdateState.Failed, service.State);
        backend.CheckResult = Task.FromResult<string?>(null);
        await service.CheckAsync();
        Assert.Equal(AppUpdateState.Current, service.State);
    }

    [Fact]
    public void UnpackagedVelopackBackendIsSafeToConstruct()
    {
        Velopack.VelopackApp.Build().SetArgs([]).SetAutoApplyOnStartup(false).Run();
        var backend = new VelopackUpdateBackend();
        Assert.False(backend.IsInstalled);
        Assert.Null(backend.PendingVersion);
    }

    private static AppUpdateService Create(IAppUpdateBackend backend) => new(backend, NullLogger<AppUpdateService>.Instance);

    private sealed class FakeBackend : IAppUpdateBackend
    {
        public bool IsInstalled { get; init; } = true;
        public string CurrentVersion => "0.1.0";
        public string? PendingVersion { get; set; }
        public Task<string?> CheckResult { get; set; } = Task.FromResult<string?>("0.2.0");
        public int CheckCount { get; private set; }
        public bool Downloaded { get; private set; }
        public bool Applied { get; private set; }
        public bool FailDownload { get; set; }
        public Task<string?> CheckAsync() { CheckCount++; return CheckResult; }
        public Task DownloadAsync(Action<int> progress)
        {
            if (FailDownload) throw new IOException("Download interrupted");
            progress(50);
            progress(100);
            Downloaded = true;
            PendingVersion = "0.2.0";
            return Task.CompletedTask;
        }
        public Task ApplyAndRestartAsync()
        {
            Assert.NotNull(PendingVersion);
            Applied = true;
            return Task.CompletedTask;
        }
    }
}
