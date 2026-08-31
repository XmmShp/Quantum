using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExamplePlugin;

public interface IExamplePluginState
{
    DateTimeOffset? StartedAt { get; }

    bool ThemeIntegrationActive { get; }
}

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(IQuantumPlugin), typeof(IExamplePluginState)])]
public sealed class ExamplePluginState : IQuantumPlugin, IExamplePluginState
{
    public DateTimeOffset? StartedAt { get; private set; }

    public bool ThemeIntegrationActive { get; private set; }

    public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        StartedAt = DateTimeOffset.Now;
        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        ThemeIntegrationActive = environment.IsIntegrationActive(
            "quantum.plugin.example",
            "quantum.plugin.theme");
        return Task.CompletedTask;
    }
}
