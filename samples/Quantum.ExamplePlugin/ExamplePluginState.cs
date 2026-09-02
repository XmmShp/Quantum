using Quantum.Plugin.Abstraction;

namespace Quantum.ExamplePlugin;

public interface IExamplePluginState
{
    DateTimeOffset? StartedAt { get; }

    bool WebPluginAvailable { get; }

    bool IsRunning { get; }

    int WebHandshakeCount { get; }

    string? LastWebPluginId { get; }
}

public sealed record ExamplePluginHandshake(
    string Message,
    int Sequence,
    DateTimeOffset DotNetStartedAt,
    bool WebPluginAvailable);

public sealed class ExamplePluginState
{
    private int _webHandshakeCount;
    private string? _lastWebPluginId;

    public DateTimeOffset? StartedAt { get; private set; }

    public bool WebPluginAvailable { get; private set; }

    public bool IsRunning { get; private set; }

    public int WebHandshakeCount => Volatile.Read(ref _webHandshakeCount);

    public string? LastWebPluginId => Volatile.Read(ref _lastWebPluginId);

    internal void Start(DateTimeOffset startedAt, bool webPluginAvailable)
    {
        StartedAt = startedAt;
        IsRunning = true;
        WebPluginAvailable = webPluginAvailable;
    }

    internal void Stop()
    {
        IsRunning = false;
    }

    internal string CreateDependencyGreeting(PluginId callerPluginId)
    {
        if (!IsRunning || StartedAt is null)
        {
            throw new InvalidOperationException("The .NET example plugin is not running.");
        }

        return $"你好，{callerPluginId}！这条消息来自 {PluginId.Of("quantum.plugin.example")} 的 DI 服务。";
    }

    internal Task<ExamplePluginHandshake> CreateWebHandshakeAsync(
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
            WebPluginAvailable));
    }
}
