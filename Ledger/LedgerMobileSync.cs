using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quantum.OfficialPlugins.Ledger.Rpc;

namespace Quantum.OfficialPlugins.Ledger;

/// <summary>Refreshes read-only declarations in fresh RPC scopes; never starts business operations.</summary>
public sealed class LedgerMobileSync : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LedgerMobileSync> _logger;

    public LedgerMobileSync(IServiceScopeFactory scopes, ILogger<LedgerMobileSync> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public void Start()
    {
        if (_loop is { IsCompleted: false })
        {
            return;
        }
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _loop = Task.Run(() => RunAsync(token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    scope.ServiceProvider.ResolveDaemonServices();
                    await scope.ServiceProvider.GetRequiredService<ILedgerMobilePages>().PublishAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Unable to update mobile page; retrying automatically.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
        }
        if (_loop is not null)
        {
            await _loop;
        }
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
