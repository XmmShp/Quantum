using Microsoft.Extensions.DependencyInjection;

namespace Quantum.ExamplePlugin;

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(IExamplePluginState)])]
public sealed class ExamplePluginStateService(ExamplePluginState state) : IExamplePluginState
{
    public DateTimeOffset? StartedAt => state.StartedAt;

    public bool WebIntegrationActive => state.WebIntegrationActive;

    public bool IsRunning => state.IsRunning;

    public int WebHandshakeCount => state.WebHandshakeCount;

    public string? LastWebPluginId => state.LastWebPluginId;

    public Task<ExamplePluginHandshake> CreateWebHandshakeAsync(
        string webPluginId,
        CancellationToken cancellationToken = default)
        => state.CreateWebHandshakeAsync(webPluginId, cancellationToken);
}
