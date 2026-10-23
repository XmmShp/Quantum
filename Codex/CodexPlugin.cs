using Microsoft.Extensions.DependencyInjection;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.UI.Mobile;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex;

public sealed class CodexPlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddRpcServer<CodexRpcService>();
        services.AddScoped<ICodexMobilePages, CodexMobilePages>();
        services.AddScoped<ICodexRpcServiceClient, LocalCodexRpcServiceClient>();
        services.AddSingleton<ICodexRuntime, CodexRuntime>();
        services.AddSingleton<ICodexRuntimeEvents>(provider => provider.GetRequiredService<ICodexRuntime>());
    }

    public static async Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (services.GetService<IQuantumMobilePages>() is not null)
        {
            await services.GetRequiredService<ICodexMobilePages>().PublishAsync(cancellationToken);
        }
    }

    public static async Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        services.GetService<IQuantumMobilePages>()?.Remove("codex");
        if (services.GetService<ICodexRuntime>() is IAsyncDisposable service)
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }
}
