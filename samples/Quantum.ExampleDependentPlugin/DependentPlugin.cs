using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExampleDependentPlugin;

/// <summary>
/// Demonstrates that a manifest dependency is available before this plugin starts.
/// </summary>
public sealed class DependentPlugin : IQuantumPlugin
{
    private static readonly PluginId CurrentPluginId = PluginId.Of("quantum.plugin.example-dependent");
    private static readonly PluginId RequiredPluginId = PluginId.Of("quantum.plugin.example");
    private const string RequiredServiceType = "Quantum.ExamplePlugin.IExamplePluginState";

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

        var dependencyServices = services.GetRequiredKeyedService<IServiceProvider>(RequiredPluginId);
        dynamic examplePlugin = dependencyServices.GetService(RequiredServiceType)
            ?? throw new InvalidOperationException(
                $"Required service '{RequiredServiceType}' is not registered by '{RequiredPluginId}'.");
        string greeting = examplePlugin.CreateDependencyGreeting(CurrentPluginId);

        services.GetRequiredService<DependentPluginState>().Start(
            DateTimeOffset.Now,
            requiredPlugin,
            greeting);
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
