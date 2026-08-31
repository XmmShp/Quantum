using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quantum.ExtensionMarket.Application;

namespace Quantum.ExtensionMarket.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddExtensionMarketInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PluginStorageOptions>(
            configuration.GetSection(PluginStorageOptions.SectionName));
        services.AddSingleton<IMarketPasswordHasher, Pbkdf2MarketPasswordHasher>();
        services.AddSingleton<IPluginPackageStore, PhysicalPluginPackageStore>();
        return services;
    }
}
