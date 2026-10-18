using Microsoft.Extensions.DependencyInjection;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.Infrastructure;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex;

public sealed class CodexPlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddPluginRpcServer<CodexRpcService>();
        services.AddScoped<ICodexRpcServiceClient, LocalCodexRpcServiceClient>();
        services.AddSingleton<ICodexRuntime, CodexRuntime>();
        services.AddSingleton<ICodexRuntimeEvents>(provider => provider.GetRequiredService<ICodexRuntime>());
        services.AddTransient<CodexRpcService.EnsureAutoConnected, EnsureCodexAutoConnected>();
        services.AddTransient<CodexRpcService.GetSnapshot, GetCodexSnapshot>();
        services.AddTransient<CodexRpcService.ListModels, ListCodexModels>();
        services.AddTransient<CodexRpcService.Connect, ConnectCodex>();
        services.AddTransient<CodexRpcService.StartNewThread, StartCodexThread>();
        services.AddTransient<CodexRpcService.ChangeModel, ChangeCodexModel>();
        services.AddTransient<CodexRpcService.Send, SendCodexPrompt>();
        services.AddTransient<CodexRpcService.Disconnect, DisconnectCodex>();
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
        if (services.GetService<ICodexRuntime>() is IAsyncDisposable service)
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }
}
