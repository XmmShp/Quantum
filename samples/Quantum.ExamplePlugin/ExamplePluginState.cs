using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExamplePlugin;

public interface IExamplePluginState
{
    DateTimeOffset? StartedAt { get; }

    bool WebIntegrationActive { get; }

    bool IsRunning { get; }

    int WebHandshakeCount { get; }

    string? LastWebPluginId { get; }

    Task<ExamplePluginHandshake> CreateWebHandshakeAsync(
        string webPluginId,
        CancellationToken cancellationToken = default);
}

public sealed record ExamplePluginHandshake(
    string Message,
    int Sequence,
    DateTimeOffset DotNetStartedAt,
    bool IntegrationActive);

[AutoInject(
    ServiceLifetime.Singleton,
    RegisterTypes = [typeof(IQuantumPlugin), typeof(IExamplePluginState)])]
public sealed class ExamplePluginState : IQuantumPlugin, IExamplePluginState
{
    private int _webHandshakeCount;
    private string? _lastWebPluginId;

    public DateTimeOffset? StartedAt { get; private set; }

    public bool WebIntegrationActive { get; private set; }

    public bool IsRunning { get; private set; }

    public int WebHandshakeCount => Volatile.Read(ref _webHandshakeCount);

    public string? LastWebPluginId => Volatile.Read(ref _lastWebPluginId);

    public Task StartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var runtime = services.GetRequiredService<IQuantumPluginRuntimeContext>();
        if (File.Exists(Path.Combine(runtime.RootPath, "fail-start")))
        {
            throw new InvalidOperationException("Example plugin startup failure was requested by a marker file.");
        }

        StartedAt = DateTimeOffset.Now;
        IsRunning = true;
        var environment = services.GetRequiredService<IQuantumPluginEnvironment>();
        WebIntegrationActive = environment.IsIntegrationActive(
            "quantum.plugin.example",
            "quantum.plugin.example-web");
        return Task.CompletedTask;
    }

    public Task StopAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task<ExamplePluginHandshake> CreateWebHandshakeAsync(
        string webPluginId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webPluginId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRunning || StartedAt is null)
        {
            throw new InvalidOperationException("The .NET example plugin is not running.");
        }

        var normalizedPluginId = webPluginId.Trim();
        var sequence = Interlocked.Increment(ref _webHandshakeCount);
        Volatile.Write(ref _lastWebPluginId, normalizedPluginId);
        return Task.FromResult(new ExamplePluginHandshake(
            $"来自 .NET 插件的第 {sequence} 次握手：你好，{normalizedPluginId}！",
            sequence,
            StartedAt.Value,
            WebIntegrationActive));
    }
}
