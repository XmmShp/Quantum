using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExampleDependentPlugin;

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(DependentPluginState)])]
public sealed class DependentPluginState
{
    public DateTimeOffset? StartedAt { get; private set; }

    public QuantumPluginInfo? RequiredPlugin { get; private set; }

    public bool IsRunning { get; private set; }

    internal void Start(DateTimeOffset startedAt, QuantumPluginInfo requiredPlugin)
    {
        ArgumentNullException.ThrowIfNull(requiredPlugin);
        StartedAt = startedAt;
        RequiredPlugin = requiredPlugin;
        IsRunning = true;
    }

    internal void Stop()
    {
        IsRunning = false;
    }
}
