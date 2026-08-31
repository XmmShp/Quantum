using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Hosting;
using Quantum.Plugin.Abstraction;

namespace Quantum.Host;

public sealed class PluginLifecycleInitializationStep : IApplicationInitializationStep
{
    public TopologyComparison Compare(IApplicationInitializationStep other)
        => TopologyComparison.DoesNotMatter;

    public async Task ExecuteAsync(IHost app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services.GetRequiredService<ILogger<PluginLifecycleInitializationStep>>();
        foreach (var plugin in app.Services.GetServices<IQuantumPlugin>())
        {
            try
            {
                await plugin.StartAsync(app.Services).ConfigureAwait(false);
                if (MauiProgram.PluginDiagnosticsEnabled())
                {
                    Console.WriteLine($"[Quantum] Started plugin lifecycle {plugin.GetType().FullName}.");
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Plugin lifecycle hook {PluginType} failed. The host will continue starting.",
                    plugin.GetType().FullName);
            }
        }
    }
}
