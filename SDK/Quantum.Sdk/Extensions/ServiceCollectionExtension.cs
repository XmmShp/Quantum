using Microsoft.Extensions.DependencyInjection;
using Quantum.Sdk.Abstractions;

namespace Quantum.Sdk.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddEagerInitializeService<TService, TImplement>(this IServiceCollection services)
        where TService : class, IInitializableService
        where TImplement : class, TService
    {
        services.AddSingleton<TService, TImplement>();
        services.AddSingleton<IInitializableService>(sp => sp.GetRequiredService<TService>());
        return services;
    }
}
