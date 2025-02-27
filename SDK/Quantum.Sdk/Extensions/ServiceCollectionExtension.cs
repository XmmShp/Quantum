using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quantum.Infrastructure.Abstractions;

namespace Quantum.Infrastructure.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddEagerInitializeService<TService, TImplement>(this IServiceCollection services)
        where TService : class, IInitializableService
        where TImplement : class, TService
    {
        services.TryAddScoped<TImplement>();
        return services.AddScoped<TService, TImplement>(sp => sp.GetRequiredService<TImplement>())
                        .AddScoped<IInitializableService>(sp => sp.GetRequiredService<TImplement>());
    }
}
