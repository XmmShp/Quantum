using Microsoft.Extensions.DependencyInjection;

namespace Quantum.ExtensionMarket.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddExtensionMarketApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MarketCallerResolver>();
        services.AddScoped<AuditWriter>();
        return services;
    }
}
