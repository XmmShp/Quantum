using Microsoft.Extensions.DependencyInjection;

namespace Quantum.Platform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddQuantumPlatformApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PlatformCallerResolver>();
        services.AddScoped<AuditWriter>();
        return services;
    }
}
