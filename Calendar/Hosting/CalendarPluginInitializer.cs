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
        services.AddTransient<CalendarRpcService.ListEntries, ListCalendarEntries>();
        services.AddTransient<CalendarRpcService.ListTasks, ListCalendarTasks>();
        services.AddTransient<CalendarRpcService.GetEntry, GetCalendarEntry>();
        services.AddTransient<CalendarRpcService.CreateEntry, CreateCalendarEntry>();
        services.AddTransient<CalendarRpcService.UpdateEntry, UpdateCalendarEntry>();
        services.AddTransient<CalendarRpcService.SetCompleted, SetCalendarEntryCompleted>();
        services.AddTransient<CalendarRpcService.DeleteEntry, DeleteCalendarEntry>();
    }
}
