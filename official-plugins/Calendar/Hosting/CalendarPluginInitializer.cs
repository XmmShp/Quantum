using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Infrastructure;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Infrastructure;

namespace Quantum.OfficialPlugins.Calendar.Hosting;

public sealed class CalendarPluginInitializer : IAssemblyInitializer
{
    public static void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelCreatingContributor, CalendarDbContextModelCreatingContributor>();
        services.AddScoped<ICalendarApplicationService, CalendarApplicationService>();
    }
}
