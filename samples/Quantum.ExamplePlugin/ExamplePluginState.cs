using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExamplePlugin;

public interface IExamplePluginState
{
    DateTimeOffset? StartedAt { get; }

    bool ThemeIntegrationActive { get; }

    bool IsRunning { get; }
}

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(IQuantumPlugin), typeof(IExamplePluginState)])]
public sealed class ExamplePluginState : IQuantumPlugin, IExamplePluginState
{
    public DateTimeOffset? StartedAt { get; private set; }

    public bool ThemeIntegrationActive { get; private set; }

    public bool IsRunning { get; private set; }

    public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var runtime = services.GetRequiredService<IQuantumPluginRuntimeContext>();
        if (File.Exists(Path.Combine(runtime.RootPath, "fail-start")))
        {
            throw new InvalidOperationException("Example plugin startup failure was requested by a marker file.");
        }

        StartedAt = DateTimeOffset.Now;
        IsRunning = true;
        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        ThemeIntegrationActive = environment.IsIntegrationActive(
            "quantum.plugin.example",
            "quantum.plugin.theme");
        return Task.CompletedTask;
    }

    public Task StopAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        IsRunning = false;
        return Task.CompletedTask;
    }
}
