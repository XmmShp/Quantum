using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using Quantum.Plugins;

namespace Microsoft.Extensions.DependencyInjection;

public static class PluginEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddQuantumPluginEventBus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNOFAbstraction();
        if (services.Any(static descriptor =>
                descriptor.ServiceType == typeof(PluginEventBusRegistrationMarker)))
        {
            return services;
        }

        services.AddSingleton<PluginEventBusRegistrationMarker>();
        services.TryAddSingleton<PluginEventHub>();
        services.TryAddSingleton(static provider => new QuantumPluginEventBusFactory(
            provider.GetRequiredService<PluginEventHub>()));
        services.TryAddTransient<PluginEventTransportHandler>();
        services.GetOrAddSingleton<EventHandlerRegistry>().Add(
            new EventHandlerRegistration(
                typeof(PluginEventTransportHandler),
                typeof(PluginEventTransportMessage)));
        return services;
    }

    private sealed class PluginEventBusRegistrationMarker;
}
