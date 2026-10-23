using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Infrastructure;
using Quantum.OfficialPlugins.Calendar.Application;

namespace Quantum.OfficialPlugins.Calendar;

public sealed class CalendarPluginInitializer : IAssemblyInitializer
{
    public static void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelCreatingContributor, CalendarDbContextModelCreatingContributor>();
        services.AddRpcServer<CalendarRpcService>();
        services.AddScoped<ICalendarRpcServiceClient, LocalCalendarRpcServiceClient>();
    }
}
