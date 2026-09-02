namespace Quantum.Plugins;

/// <summary>
/// Creates active, explicitly owned EventBus instances for non-.NET plugin runtimes.
/// </summary>
public sealed class QuantumPluginEventBusFactory
{
    private readonly PluginEventHub _hub;

    internal QuantumPluginEventBusFactory(PluginEventHub hub)
    {
        _hub = hub;
    }

    public QuantumPluginEventBusHandle Create(QuantumPluginInfo plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var bus = new PluginEventBus(plugin, _hub);
        bus.Resume();
        return new QuantumPluginEventBusHandle(bus);
    }
}

/// <summary>
/// Owns an active EventBus instance for one external plugin runtime generation.
/// </summary>
public sealed class QuantumPluginEventBusHandle : IQuantumEventBus, IDisposable, IAsyncDisposable
{
    private readonly PluginEventBus _bus;

    internal QuantumPluginEventBusHandle(PluginEventBus bus)
    {
        _bus = bus;
    }

    public IQuantumPublisher<TMessage> CreatePublisher<TMessage>(QuantumTopic topic)
        => _bus.CreatePublisher<TMessage>(topic);

    public IQuantumSubscription Subscribe(
        QuantumTopic topic,
        Func<QuantumEvent, CancellationToken, Task> handler)
        => _bus.Subscribe(topic, handler);

    public void Dispose() => _bus.Dispose();

    public ValueTask DisposeAsync() => _bus.DisposeAsync();
}
