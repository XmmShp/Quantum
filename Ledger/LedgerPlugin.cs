using Microsoft.Extensions.DependencyInjection;
using NOF.Infrastructure;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Ledger;

public sealed class LedgerPlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelCreatingContributor, LedgerModel>();
        services.AddRpcServer<LedgerRpcService>();
        services.AddMapping<LedgerEntry, LedgerEntryProjection>(LedgerMappings.Projection);
        services.AddScoped<ILedgerRpcServiceClient, LocalLedgerRpcServiceClient>();
    }

    public static Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public static Task StopAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
