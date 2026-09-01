using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Infrastructure;
using Quantum.ExampleCalendarPlugin.Application;
using Quantum.ExampleCalendarPlugin.Infrastructure;

namespace Quantum.ExampleCalendarPlugin.Hosting;

public sealed class CalendarPluginInitializer : IAssemblyInitializer
{
    public static void Initialize(IServiceCollection services)
    {
        services.AddSingleton<
            IDbContextModelCreatingContributor,
            CalendarDbContextModelCreatingContributor>();
        services.AddScoped<ICalendarItemApplicationService, CalendarItemApplicationService>();
    }
}
