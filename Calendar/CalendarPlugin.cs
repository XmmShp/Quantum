using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Calendar;

public sealed class CalendarPlugin : IQuantumPlugin
{
    public static Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public static Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
