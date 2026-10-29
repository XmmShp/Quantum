using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Infrastructure;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.UI.Mobile;

namespace Quantum.OfficialPlugins.Calendar;

public sealed class CalendarPluginInitializer : IAssemblyInitializer
{
    public static void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelCreatingContributor, CalendarDbContextModelCreatingContributor>();
        services.AddRpcServer<CalendarRpcService>();
        services.AddScoped<ICalendarMobilePages, CalendarMobilePages>();
        services.AddSingleton<CalendarMobileSync>();
        services.AddScoped<ICalendarRpcServiceClient, LocalCalendarRpcServiceClient>();
    }
}
