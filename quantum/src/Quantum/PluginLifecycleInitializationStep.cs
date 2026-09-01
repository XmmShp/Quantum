using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Hosting;
using Quantum.Plugins;

namespace Quantum;

public sealed class PluginLifecycleInitializationStep : IApplicationInitializationStep
{
    public TopologyComparison Compare(IApplicationInitializationStep other)
        => TopologyComparison.DoesNotMatter;

    public async Task ExecuteAsync(IHost app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services.GetRequiredService<ILogger<PluginLifecycleInitializationStep>>();
        try
        {
            var manager = app.Services.GetRequiredService<IPluginRuntimeManager>();
            await manager.InitializeAsync(app.Services).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Plugin runtime initialization failed. The host will continue without dynamic plugins.");
        }
    }
}
