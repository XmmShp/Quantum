using Microsoft.Extensions.DependencyInjection;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Calendar;

public sealed class CalendarPlugin : IQuantumPlugin
{
    public static async Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (services.GetService<IQuantumMobilePages>() is not null)
        {
            await services.GetRequiredService<ICalendarMobilePages>().PublishAsync(cancellationToken);
            services.GetRequiredService<CalendarMobileSync>().Start();
        }
    }

    public static async Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<CalendarMobileSync>().DisposeAsync();
        services.GetService<IQuantumMobilePages>()?.Remove("calendar");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
