using Microsoft.Extensions.DependencyInjection;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex;

public sealed class CodexPlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICodexIntegrationService, CodexIntegrationService>();
    }

    public static Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public static async Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        if (services.GetService<ICodexIntegrationService>() is IAsyncDisposable service)
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }
}
