using Microsoft.Extensions.DependencyInjection;
using NOF.Infrastructure;
using Quantum.Plugin.Abstraction;
using Quantum.OfficialPlugins.Ledger.Rpc;

namespace Quantum.OfficialPlugins.Ledger;

public sealed class LedgerPlugin : IQuantumPlugin
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDbContextModelCreatingContributor, LedgerModel>();
        services.AddScoped<LedgerService>();
        services.AddScoped<LedgerSession>();
        services.AddTransient<LedgerRpcService.ListEntries, ListLedgerEntries>();
        services.AddTransient<LedgerRpcService.GetEntry, GetLedgerEntry>();
        services.AddTransient<LedgerRpcService.GetSummary, GetLedgerSummary>();
        services.AddTransient<LedgerRpcService.CreateEntry, CreateLedgerEntry>();
        services.AddTransient<LedgerRpcService.UpdateEntry, UpdateLedgerEntry>();
        services.AddTransient<LedgerRpcService.DeleteEntry, DeleteLedgerEntry>();
        services.AddTransient<LedgerRpcService.ExportCsv, ExportLedgerCsv>();
        services.AddTransient<LedgerRpcService.ImportCsv, ImportLedgerCsv>();
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

internal sealed class LedgerModel : IDbContextModelCreatingContributor
{
    public void Configure(IDbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("OfficialLedgerEntries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Date).IsRequired();
            entity.Property(item => item.Kind).IsRequired();
            entity.Property(item => item.AmountInFen).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Note).HasMaxLength(1000).IsRequired();
            entity.HasIndex(item => item.Date);
        });
    }
}
