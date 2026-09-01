using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExamplePlugin;

/// <summary>
/// Starts and stops the plugin through the runtime scope supplied by the host.
/// Business state belongs to regular services, not to this static bootstrap.
/// </summary>
public sealed class ExamplePlugin : IQuantumPlugin
{
    public static Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtime = services.GetRequiredService<IQuantumPluginRuntimeContext>();
        if (File.Exists(Path.Combine(runtime.RootPath, "fail-start")))
        {
            throw new InvalidOperationException("Example plugin startup failure was requested by a marker file.");
        }

        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        services.GetRequiredService<ExamplePluginState>().Start(
            DateTimeOffset.Now,
            environment.IsIntegrationActive(
                "quantum.plugin.example",
                "quantum.plugin.example-web"));

        return Task.CompletedTask;
    }

    public static Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        services.GetRequiredService<ExamplePluginState>().Stop();
        return Task.CompletedTask;
    }
}
