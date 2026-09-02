using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;

namespace Quantum.Plugins;

internal sealed class PluginEventBus(
    QuantumPluginInfo plugin,
    PluginEventHub hub) : IQuantumEventBus, IDisposable, IAsyncDisposable
{
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        // A per-runtime resolver prevents System.Text.Json's default shared reflection resolver
        // from caching message types that belong to a collectible plugin load context.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    private readonly object _gate = new();
    private readonly Dictionary<Guid, IDisposable> _subscriptions = [];
    private int _state;

    public IQuantumPublisher<TMessage> CreatePublisher<TMessage>(QuantumTopic topic)
    {
        var validatedTopic = EnsureValidTopic(topic);
        ThrowIfDisposed();
        return new PluginEventPublisher<TMessage>(this, validatedTopic);
    }

    public IQuantumSubscription Subscribe(
        QuantumTopic topic,
        Func<QuantumEvent, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var validatedTopic = EnsureValidTopic(topic);
        return SubscribeCore(
            validatedTopic,
            (message, cancellationToken) => HandleAsync(message, handler, cancellationToken));
    }

    private IQuantumSubscription SubscribeCore(
        QuantumTopic topic,
        Func<PluginEventTransportMessage, CancellationToken, Task> handler)
    {
        var id = Guid.NewGuid();
        lock (_gate)
        {
            ThrowIfDisposed();
            var transportSubscription = hub.Subscribe(topic, handler);
            _subscriptions.Add(id, transportSubscription);
        }

        return new PluginEventSubscription(this, id, topic);
    }

    internal void Resume()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            Volatile.Write(ref _state, 1);
        }
    }

    internal void Pause()
    {
        lock (_gate)
        {
            if (_state != 2)
            {
                Volatile.Write(ref _state, 0);
            }
        }
    }

    public void Dispose()
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            if (_state == 2)
            {
                return;
            }

            Volatile.Write(ref _state, 2);
            subscriptions = _subscriptions.Values.ToArray();
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        // System.Text.Json also caches generated member-accessor delegates process-wide.
        // Reset both caches so plugin message types cannot root a collectible load context.
        ClearJsonSerializerCaches(_serializerOptions);
        ClearJsonMemberAccessorCaches(null);
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask PublishAsync<TMessage>(
        QuantumTopic topic,
        TMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(
            message,
            message.GetType(),
            _serializerOptions);
        await hub.PublishAsync(
                new PluginEventTransportMessage(
                    Guid.NewGuid(),
                    topic,
                    payload,
                    plugin,
                    DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleAsync(
        PluginEventTransportMessage message,
        Func<QuantumEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        if (!IsActive)
        {
            return;
        }

        await handler(
                new QuantumEvent(
                    message.Id,
                    message.Topic,
                    message.Payload,
                    message.Publisher,
                    message.PublishedAt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsActive => Volatile.Read(ref _state) == 1;

    private void RemoveSubscription(Guid id)
    {
        IDisposable? subscription;
        lock (_gate)
        {
            _subscriptions.Remove(id, out subscription);
        }

        subscription?.Dispose();
    }

    private void EnsureActive()
    {
        var state = Volatile.Read(ref _state);
        ObjectDisposedException.ThrowIf(state == 2, this);
        if (state != 1)
        {
            throw new InvalidOperationException(
                $"The event bus for plugin '{plugin.Id}' is not active.");
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _state) == 2, this);

    private static QuantumTopic EnsureValidTopic(QuantumTopic topic)
        => QuantumTopic.Of((string)topic);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ClearCaches")]
    private static extern void ClearJsonSerializerCaches(JsonSerializerOptions options);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ClearMemberAccessorCaches")]
    private static extern void ClearJsonMemberAccessorCaches(DefaultJsonTypeInfoResolver? resolver);

    private sealed class PluginEventPublisher<TMessage>(
        PluginEventBus owner,
        QuantumTopic topic) : IQuantumPublisher<TMessage>
    {
        public QuantumTopic Topic => topic;

        public ValueTask PublishAsync(
            TMessage message,
            CancellationToken cancellationToken = default)
            => owner.PublishAsync(topic, message, cancellationToken);
    }

    private sealed class PluginEventSubscription(
        PluginEventBus owner,
        Guid id,
        QuantumTopic topic) : IQuantumSubscription
    {
        private int _disposed;

        public QuantumTopic Topic => topic;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.RemoveSubscription(id);
            }

            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class PluginEventHub(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<
        QuantumTopic,
        ConcurrentDictionary<Guid, PluginEventSink>> _subscriptions = [];

    public IDisposable Subscribe(
        QuantumTopic topic,
        Func<PluginEventTransportMessage, CancellationToken, Task> handler)
    {
        var id = Guid.NewGuid();
        var sink = new PluginEventSink(
            () => Remove(topic, id),
            handler);
        _subscriptions
            .GetOrAdd(topic, static _ => [])
            .TryAdd(id, sink);
        return sink;
    }

    public async ValueTask PublishAsync(
        PluginEventTransportMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        await publisher.PublishAsync(message, Context.Empty, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DispatchAsync(
        PluginEventTransportMessage message,
        CancellationToken cancellationToken)
    {
        if (!_subscriptions.TryGetValue(message.Topic, out var topicSubscriptions))
        {
            return;
        }

        List<Exception>? failures = null;
        foreach (var subscription in topicSubscriptions.Values.ToArray())
        {
            try
            {
                await subscription.InvokeAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                $"One or more subscribers failed while handling topic '{message.Topic}'.",
                failures);
        }
    }

    private void Remove(QuantumTopic topic, Guid id)
    {
        if (!_subscriptions.TryGetValue(topic, out var topicSubscriptions))
        {
            return;
        }

        topicSubscriptions.TryRemove(id, out _);
        if (topicSubscriptions.IsEmpty)
        {
            ((ICollection<KeyValuePair<QuantumTopic, ConcurrentDictionary<Guid, PluginEventSink>>>)
                _subscriptions).Remove(new KeyValuePair<
                    QuantumTopic,
                    ConcurrentDictionary<Guid, PluginEventSink>>(topic, topicSubscriptions));
        }
    }

    private sealed class PluginEventSink(
        Action remove,
        Func<PluginEventTransportMessage, CancellationToken, Task> handler) : IDisposable
    {
        private int _disposed;

        public Task InvokeAsync(
            PluginEventTransportMessage message,
            CancellationToken cancellationToken)
            => Volatile.Read(ref _disposed) == 0
                ? handler(message, cancellationToken)
                : Task.CompletedTask;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                remove();
            }
        }
    }
}

internal sealed class PluginEventTransportHandler(PluginEventHub hub)
    : InMemoryEventHandler<PluginEventTransportMessage>
{
    public override Task HandleAsync(
        PluginEventTransportMessage @event,
        Context context,
        CancellationToken cancellationToken)
        => hub.DispatchAsync(@event, cancellationToken);
}

internal sealed record PluginEventTransportMessage(
    Guid Id,
    QuantumTopic Topic,
    JsonElement Payload,
    QuantumPluginInfo Publisher,
    DateTimeOffset PublishedAt);
