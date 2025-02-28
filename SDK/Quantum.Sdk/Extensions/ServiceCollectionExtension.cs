using Microsoft.Extensions.DependencyInjection;
using Quantum.Sdk.Abstractions;

namespace Quantum.Sdk.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddEagerInitializeService<T>(this IServiceCollection services) where T : class, IInitializableService
    {
        services.AddSingleton<IInitializableService, T>();
        return services;
    }
}
