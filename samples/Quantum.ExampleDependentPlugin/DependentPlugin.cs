using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExampleDependentPlugin;

/// <summary>
/// Demonstrates that a manifest dependency is available before this plugin starts.
/// </summary>
public sealed class DependentPlugin : IQuantumPlugin
{
    private static readonly PluginId RequiredPluginId = PluginId.Of("quantum.plugin.example");

    public static Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        var requiredPlugin = environment.LoadedPlugins.SingleOrDefault(plugin =>
            plugin.Id == RequiredPluginId);
        if (requiredPlugin is null)
        {
            throw new InvalidOperationException(
                $"Required plugin '{RequiredPluginId}' was not loaded before the dependent example.");
        }

        services.GetRequiredService<DependentPluginState>().Start(
            DateTimeOffset.Now,
            requiredPlugin);
        return Task.CompletedTask;
    }

    public static Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        services.GetRequiredService<DependentPluginState>().Stop();
        return Task.CompletedTask;
    }
}
