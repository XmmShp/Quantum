using System.Globalization;
using System.Text.Json;
using Quantum.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

// This executable uses the macOS CoreCLR runtime. Catalyst identifies as iOS,
// which Velopack does not support. Keep it inside the signed .app bundle.
if (!OperatingSystem.IsMacOS() || args.Length != 3 ||
    args[0] is not ("check" or "download" or "apply") ||
    !uint.TryParse(args[2], CultureInfo.InvariantCulture, out var parentPid) || parentPid == 0)
    return 2;

var responsePath = args[1];
var writeLock = new object();
void Write(UpdateAgentMessage message)
{
    lock (writeLock)
    {
        var temporary = responsePath + ".writing";
        File.WriteAllText(temporary, JsonSerializer.Serialize(message));
        File.Move(temporary, responsePath, overwrite: true);
    }
}

try
{
    VelopackApp.Build().SetArgs([]).SetAutoApplyOnStartup(false).Run();
    var locator = VelopackLocator.Current;
    var manager = new UpdateManager(new GithubSource("https://github.com/XmmShp/Quantum", null, false));
    if (!manager.IsInstalled) throw new InvalidOperationException("The app is not a Velopack installation.");
    string? target = manager.UpdatePendingRestart?.Version.ToString();
    switch (args[0])
    {
        case "check":
            if (target is null) target = (await manager.CheckForUpdatesAsync())?.TargetFullRelease.Version.ToString();
            break;
        case "download":
            if (manager.UpdatePendingRestart is null)
            {
                var update = await manager.CheckForUpdatesAsync()
                    ?? throw new InvalidOperationException("The update is no longer available. Check again.");
                target = update.TargetFullRelease.Version.ToString();
                await manager.DownloadUpdatesAsync(update, progress => Write(new(Progress: progress)));
            }
            break;
        case "apply":
            var pending = manager.UpdatePendingRestart ?? throw new InvalidOperationException("No prepared update.");
            // Wait for the GUI, not this short-lived agent. The GUI exits only
            // after it receives the successful handoff response below.
            UpdateExe.Apply(locator, pending, silent: false, waitPid: parentPid, restart: true);
            break;
    }
    Write(new(Completed: true, Installed: true, CurrentVersion: manager.CurrentVersion?.ToString(),
        TargetVersion: target, PendingVersion: manager.UpdatePendingRestart?.Version.ToString(), Progress: 100));
    return 0;
}
catch (Exception exception)
{
    Write(new(Completed: true, Error: exception.ToString()));
    return 1;
}
