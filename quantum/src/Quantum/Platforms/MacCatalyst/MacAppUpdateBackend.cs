using System.Runtime.InteropServices;
using System.Text.Json;
using Foundation;

namespace Quantum.Updates;

/// <summary>Runs the macOS CoreCLR updater without using unsupported Catalyst process APIs.</summary>
public sealed class MacAppUpdateBackend : IAppUpdateBackend
{
    private readonly string _bundle = NSBundle.MainBundle.BundlePath;
    private string AgentPath => Path.Combine(_bundle, "Contents", "Helpers", "Quantum.UpdateAgent", "Quantum.UpdateAgent");
    public bool IsInstalled => File.Exists(AgentPath) &&
        (File.Exists(Path.Combine(_bundle, "Contents", "MacOS", "sq.version")) ||
         File.Exists(Path.Combine(_bundle, "Contents", "Resources", "sq.version")));
    public string CurrentVersion { get; private set; } = AppInfo.Current.VersionString;
    public string? PendingVersion { get; private set; }

    public async Task<string?> CheckAsync() => (await RunAsync("check")).TargetVersion;
    public async Task DownloadAsync(Action<int> progress) => await RunAsync("download", progress);
    public async Task ApplyAndRestartAsync()
    {
        await RunAsync("apply");
        await MainThread.InvokeOnMainThreadAsync(() => Application.Current!.Quit());
    }

    private async Task<UpdateAgentMessage> RunAsync(string command, Action<int>? progress = null)
    {
        var response = Path.Combine(FileSystem.CacheDirectory, $"update-{Guid.NewGuid():N}.json");
        var pid = Spawn(AgentPath, [command, response, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        var exited = false;
        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(15);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(response))
                {
                    var message = JsonSerializer.Deserialize<UpdateAgentMessage>(await File.ReadAllTextAsync(response))
                        ?? throw new IOException("Empty update response.");
                    progress?.Invoke(message.Progress);
                    if (message.Completed)
                    {
                        if (message.Error is not null) throw new IOException(message.Error);
                        CurrentVersion = message.CurrentVersion ?? CurrentVersion;
                        PendingVersion = message.PendingVersion;
                        return message;
                    }
                }
                // Reap children; a crashed helper must not leave the UI spinning.
                if (waitpid(pid, out _, 1) == pid)
                {
                    exited = true;
                    // The final file can have arrived after the read above.
                    if (!File.Exists(response)) throw new IOException("The update agent exited without a response.");
                    var final = JsonSerializer.Deserialize<UpdateAgentMessage>(await File.ReadAllTextAsync(response));
                    if (final?.Completed != true) throw new IOException("The update agent stopped before completing the operation.");
                    if (final.Error is not null) throw new IOException(final.Error);
                    CurrentVersion = final.CurrentVersion ?? CurrentVersion;
                    PendingVersion = final.PendingVersion;
                    return final;
                }
                await Task.Delay(150);
            }
            throw new TimeoutException("The update agent timed out.");
        }
        finally
        {
            if (!exited)
            {
                // Agent has already handed the work to UpdateMac on success.
                // Reap it off the UI thread before deleting the response file.
                await Task.Run(() =>
                {
                    if (waitpid(pid, out _, 1) == 0)
                    {
                        kill(pid, 15);
                        waitpid(pid, out _, 0);
                    }
                });
            }
            File.Delete(response);
            File.Delete(response + ".writing");
        }
    }

    private static int Spawn(string path, string[] arguments)
    {
        var strings = new[] { path }.Concat(arguments).Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var argv = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        try
        {
            Marshal.Copy(strings, 0, argv, strings.Length);
            Marshal.WriteIntPtr(argv, strings.Length * IntPtr.Size, IntPtr.Zero);
            var error = posix_spawn(out var pid, path, IntPtr.Zero, IntPtr.Zero, argv, Marshal.ReadIntPtr(_NSGetEnviron()));
            if (error != 0) throw new IOException($"Cannot launch the update agent (errno {error}).");
            return pid;
        }
        finally
        {
            Marshal.FreeHGlobal(argv);
            foreach (var value in strings) Marshal.FreeCoTaskMem(value);
        }
    }

    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    [DllImport(LibSystem)]
    private static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr actions, IntPtr attributes, IntPtr argv, IntPtr environment);
    [DllImport(LibSystem)] private static extern IntPtr _NSGetEnviron();
    [DllImport(LibSystem)] private static extern int waitpid(int pid, out int status, int options);
    [DllImport(LibSystem)] private static extern int kill(int pid, int signal);
}
