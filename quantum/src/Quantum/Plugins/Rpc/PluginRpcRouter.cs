using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Quantum.Plugins;

public sealed class PluginRpcRouter : IDisposable
{
    private readonly PluginCatalog _catalog;
    private readonly ILogger<PluginRpcRouter> _logger;
    private PluginRpcRegistry _registry;
    private int _disposed;

    public PluginRpcRouter(
        PluginCatalog catalog,
        ILogger<PluginRpcRouter> logger)
    {
        _catalog = catalog;
        _logger = logger;
        _registry = CreateRegistry();
        _catalog.Changed += HandleCatalogChanged;
    }

    internal Task<PluginRpcDispatchResult> InvokeAsync(
        string rpcName,
        JsonElement payload,
        PluginRpcCallContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return Volatile.Read(ref _registry).InvokeAsync(
            rpcName,
            payload,
            context,
            expectsValue: null,
            cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _catalog.Changed -= HandleCatalogChanged;
            Volatile.Write(ref _registry, PluginRpcRegistry.Empty);
        }
    }

    private void HandleCatalogChanged(object? sender, EventArgs arguments)
        => Volatile.Write(ref _registry, CreateRegistry());

    private PluginRpcRegistry CreateRegistry()
        => PluginRpcRegistry.Create(
            _catalog.Plugins
                .Select(static plugin => plugin.RpcRuntime)
                .OfType<PluginRpcRuntime>(),
            _logger);
}
