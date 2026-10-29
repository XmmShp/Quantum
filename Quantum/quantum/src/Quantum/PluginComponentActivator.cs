using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugins;

namespace Quantum;

public sealed class PluginComponentActivator(
    IServiceProvider hostServices,
    IPluginRuntimeManager pluginRuntimeManager) : IComponentActivator
{
    public IComponent CreateInstance(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        var pluginServices = pluginRuntimeManager.GetPluginServices(componentType.Assembly);
        var services = pluginServices is null
            ? hostServices
            : new FallbackServiceProvider(pluginServices, hostServices);
        return ActivatorUtilities.CreateInstance(services, componentType) as IComponent
            ?? throw new ArgumentException(
                $"The type '{componentType.FullName}' does not implement IComponent.",
                nameof(componentType));
    }

    private sealed class FallbackServiceProvider(
        IServiceProvider primary,
        IServiceProvider fallback) : IServiceProvider, IServiceProviderIsService
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider)
                || serviceType == typeof(IServiceProviderIsService))
            {
                return this;
            }

            return primary.GetService(serviceType) ?? fallback.GetService(serviceType);
        }

        public bool IsService(Type serviceType)
            => IsService(primary, serviceType) || IsService(fallback, serviceType);

        private static bool IsService(IServiceProvider provider, Type serviceType)
            => provider.GetService<IServiceProviderIsService>()?.IsService(serviceType)
                ?? provider.GetService(serviceType) is not null;
    }
}
