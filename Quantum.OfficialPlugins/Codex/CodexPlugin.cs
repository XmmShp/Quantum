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
        services.AddRpcServer<CodexSpeechRpcService>();
        services.AddSingleton<ICodexSpeechRecognizer, SystemSpeechRecognizer>();
        services.AddScoped<ICodexMobilePages, CodexMobilePages>();
        services.AddSingleton<CodexMobileSync>();
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
            services.GetRequiredService<CodexMobileSync>().Start();
        }
    }

    public static async Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<CodexMobileSync>().DisposeAsync();
        services.GetService<IQuantumMobilePages>()?.Remove("codex");
        if (services.GetService<ICodexRuntime>() is IAsyncDisposable service)
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }
}
