using Microsoft.Extensions.DependencyInjection;
using NOF.Infrastructure;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.OfficialPlugins.Ledger.UI.Mobile;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Ledger;

public sealed class LedgerPlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelCreatingContributor, LedgerModel>();
        services.AddRpcServer<LedgerRpcService>();
        services.AddScoped<ILedgerMobilePages, LedgerMobilePages>();
        services.AddSingleton<LedgerMobileSync>();
        services.AddMapping<LedgerEntry, LedgerEntryProjection>(LedgerMappings.Projection);
        services.AddScoped<ILedgerRpcServiceClient, LocalLedgerRpcServiceClient>();
    }

    public static async Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (services.GetService<IQuantumMobilePages>() is not null)
        {
            await services.GetRequiredService<ILedgerMobilePages>().PublishAsync(cancellationToken);
            services.GetRequiredService<LedgerMobileSync>().Start();
        }
    }

    public static async Task StopAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<LedgerMobileSync>().DisposeAsync();
        services.GetService<IQuantumMobilePages>()?.Remove("ledger");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
