using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;
using MobileUi = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.ExamplePlugin;

/// <summary>
/// Starts and stops the plugin through the runtime scope supplied by the host.
/// Business state belongs to regular services, not to this static bootstrap.
/// </summary>
public sealed class ExamplePlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ExamplePluginState>();
    }

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
            environment.IsPluginLoaded(PluginId.Of("quantum.plugin.example-web")));

        services.GetRequiredService<IQuantumMobilePages>().Publish(MobileUi.Page("hello", "Quantum on your phone",
            MobileUi.VStack("root",
                MobileUi.Text("title", "Connected to your desktop").Appearance("title"),
                MobileUi.Text("description", "This button invokes the existing desktop plugin RPC."),
                MobileUi.Button("greet", "Say hello").OnClick(MobileUi.Rpc("quantum.plugin.example.example.greet")))));

        return Task.CompletedTask;
    }

    public static Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        services.GetRequiredService<ExamplePluginState>().Stop();
        services.GetRequiredService<IQuantumMobilePages>().Remove("hello");
        return Task.CompletedTask;
    }
}
