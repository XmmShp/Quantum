namespace Quantum.OfficialPlugins.Ledger.Rpc;

/// <summary>Presentation port for rebuilding the plugin's published mobile page.</summary>
public interface ILedgerMobilePages
{
    Task PublishAsync(CancellationToken cancellationToken = default);
}
