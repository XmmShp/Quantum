using Quantum.ExtensionMarket.Application;

namespace Quantum.ExtensionMarket;

public static class ExtensionMarketServiceCollectionExtensions
{
    public static IServiceCollection AddExtensionMarketServices(
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
