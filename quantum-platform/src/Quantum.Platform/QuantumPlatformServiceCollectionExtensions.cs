using Quantum.Platform.Application;

namespace Quantum.Platform;

public static class QuantumPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddQuantumPlatformServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PluginStorageOptions>(
            configuration.GetSection(PluginStorageOptions.SectionName));
        services.AddSingleton<IPlatformPasswordHasher, Pbkdf2PlatformPasswordHasher>();
        services.AddSingleton<IPluginPackageStore, PhysicalPluginPackageStore>();
        return services;
    }
}
