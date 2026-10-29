using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Quantum.Plugin.Abstraction;

/// <summary>
/// Provides ROS-style publish/subscribe messaging between Quantum plugins through named topics.
/// </summary>
/// <remarks>
/// The bus is local to the current Quantum host. Messages are delivered to the subscriptions that
/// are active when <see cref="IQuantumPublisher{TMessage}.PublishAsync"/> is called; messages are not
/// retained or replayed.
/// </remarks>
public interface IQuantumEventBus
{
    /// <summary>
    /// Creates a publisher for <paramref name="topic"/>.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TMessage"/> controls plugin-side serialization only. Topic routing does
    /// not use or transmit CLR type identity.
    /// </remarks>
    IQuantumPublisher<TMessage> CreatePublisher<TMessage>(QuantumTopic topic);

    /// <summary>
    /// Subscribes to <paramref name="topic"/> until the returned handle is disposed.
    /// </summary>
    IQuantumSubscription Subscribe(
        QuantumTopic topic,
        Func<QuantumEvent, CancellationToken, Task> handler);
}

/// <summary>
/// Publishes messages of <typeparamref name="TMessage"/> to one topic.
/// </summary>
public interface IQuantumPublisher<in TMessage>
{
    /// <summary>
    /// Gets the normalized topic name.
    /// </summary>
    QuantumTopic Topic { get; }

    /// <summary>
    /// Publishes a message and waits for the current in-process subscribers to finish.
    /// </summary>
    ValueTask PublishAsync(TMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents one topic subscription.
/// </summary>
public interface IQuantumSubscription : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the normalized topic name.
    /// </summary>
    QuantumTopic Topic { get; }
}

/// <summary>
/// Contains a JSON payload and host-assigned publication metadata.
/// </summary>
public sealed record QuantumEvent(
    Guid Id,
    QuantumTopic Topic,
    JsonElement Payload,
    QuantumPluginInfo Publisher,
    DateTimeOffset PublishedAt)
{
    /// <summary>
    /// Deserializes <see cref="Payload"/> using web JSON defaults unless options are supplied.
    /// </summary>
    public TMessage? Deserialize<TMessage>(JsonSerializerOptions? options = null)
        => Payload.Deserialize<TMessage>(CreateSerializerOptions(options));

    /// <summary>
    /// Deserializes <see cref="Payload"/> and rejects a JSON <see langword="null"/> result.
    /// </summary>
    /// <exception cref="JsonException">The payload is invalid or deserializes to null.</exception>
    public TMessage DeserializeRequired<TMessage>(JsonSerializerOptions? options = null)
        => Deserialize<TMessage>(options)
            ?? throw new JsonException(
                $"Topic '{Topic}' produced a null {typeof(TMessage).FullName} payload.");

    /// <summary>
    /// Deserializes <see cref="Payload"/> to a CLR type selected at runtime.
    /// </summary>
    public object? Deserialize(Type messageType, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        return Payload.Deserialize(messageType, CreateSerializerOptions(options));
    }

    /// <summary>
    /// Attempts to deserialize <see cref="Payload"/> without propagating malformed JSON errors.
    /// </summary>
    public bool TryDeserialize<TMessage>(
        out TMessage? message,
        JsonSerializerOptions? options = null)
    {
        try
        {
            message = Deserialize<TMessage>(options);
            return true;
        }
        catch (JsonException)
        {
            message = default;
            return false;
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions(JsonSerializerOptions? options)
        => options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
}
