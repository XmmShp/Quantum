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
        services.AddPluginRpcServer<LedgerRpcService>();
        services.AddScoped<ILedgerRpcServiceClient, LocalLedgerRpcServiceClient>();
        services.AddTransient<LedgerRpcService.ListEntries, ListLedgerEntries>();
        services.AddTransient<LedgerRpcService.GetEntry, GetLedgerEntry>();
        services.AddTransient<LedgerRpcService.GetRelatedEntries, GetRelatedLedgerEntries>();
        services.AddTransient<LedgerRpcService.GetSummary, GetLedgerSummary>();
        services.AddTransient<LedgerRpcService.CreateEntry, CreateLedgerEntry>();
        services.AddTransient<LedgerRpcService.UpdateEntry, UpdateLedgerEntry>();
        services.AddTransient<LedgerRpcService.DeleteEntry, DeleteLedgerEntry>();
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
