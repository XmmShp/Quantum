using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Quantum.Plugin.Abstraction;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class PluginEventBusTests
{
    [Fact]
    public async Task PublishAsync_RoutesByTopicThroughNofAndMapsAcrossMessageTypes()
    {
        await using var host = CreateHost();
        var hub = host.GetRequiredService<PluginEventHub>();
        await using var publisherBus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.publisher", "1.0.0"),
            hub);
        await using var subscriberBus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.subscriber", "2.0.0"),
            hub);
        publisherBus.Resume();
        subscriberBus.Resume();

        QuantumEvent? received = null;
        SubscriberMessage? receivedMessage = null;
        await using var subscription = subscriberBus.Subscribe(
            Topic("sensors.camera.status"),
            (@event, _) =>
            {
                received = @event;
                receivedMessage = @event.DeserializeRequired<SubscriberMessage>();
                return Task.CompletedTask;
            });
        var publisher = publisherBus.CreatePublisher<PublisherMessage>(
            Topic("sensors.camera.status"));

        await publisher.PublishAsync(new PublisherMessage("ready", 7));

        Assert.Equal(Topic("sensors.camera.status"), publisher.Topic);
        Assert.Equal(Topic("sensors.camera.status"), subscription.Topic);
        Assert.NotNull(received);
        Assert.NotNull(receivedMessage);
        Assert.NotEqual(Guid.Empty, received.Id);
        Assert.Equal("ready", receivedMessage.State);
        Assert.Equal(7, receivedMessage.Sequence);
        Assert.Equal("ready", received.Payload.GetProperty("state").GetString());
        Assert.Equal("quantum.plugin.publisher", received.Publisher.Id);
        Assert.Equal("1.0.0", received.Publisher.Version);
        Assert.InRange(
            received.PublishedAt,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task PublishAsync_DeliversOnlyToActiveSubscriptionsOnTheSameTopic()
    {
        await using var host = CreateHost();
        var hub = host.GetRequiredService<PluginEventHub>();
        await using var bus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.example", "1.0.0"),
            hub);
        bus.Resume();
        var matchingCalls = 0;
        var otherCalls = 0;
        var matching = bus.Subscribe(
            Topic("status"),
            (_, _) =>
            {
                matchingCalls++;
                return Task.CompletedTask;
            });
        await using var other = bus.Subscribe(
            Topic("other"),
            (_, _) =>
            {
                otherCalls++;
                return Task.CompletedTask;
            });
        var publisher = bus.CreatePublisher<PublisherMessage>(Topic("status"));

        await publisher.PublishAsync(new PublisherMessage("first", 1));
        matching.Dispose();
        await publisher.PublishAsync(new PublisherMessage("second", 2));

        Assert.Equal(1, matchingCalls);
        Assert.Equal(0, otherCalls);
    }

    [Fact]
    public async Task Pause_SuspendsDeliveryAndPublishingUntilResume()
    {
        await using var host = CreateHost();
        var hub = host.GetRequiredService<PluginEventHub>();
        await using var publisherBus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.publisher", "1.0.0"),
            hub);
        await using var subscriberBus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.subscriber", "1.0.0"),
            hub);
        publisherBus.Resume();
        subscriberBus.Resume();
        var calls = 0;
        await using var subscription = subscriberBus.Subscribe(
            Topic("status"),
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            });
        var publisher = publisherBus.CreatePublisher<PublisherMessage>(Topic("status"));

        subscriberBus.Pause();
        await publisher.PublishAsync(new PublisherMessage("ignored", 1));
        subscriberBus.Resume();
        await publisher.PublishAsync(new PublisherMessage("received", 2));
        publisherBus.Pause();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await publisher.PublishAsync(new PublisherMessage("rejected", 3)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PublishAsync_ContinuesOtherSubscribersAndReportsFailures()
    {
        await using var host = CreateHost();
        var hub = host.GetRequiredService<PluginEventHub>();
        await using var bus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.example", "1.0.0"),
            hub);
        bus.Resume();
        await using var failing = bus.Subscribe(
            Topic("status"),
            (_, _) => throw new InvalidOperationException("subscriber failed"));
        var successfulCalls = 0;
        await using var successful = bus.Subscribe(
            Topic("status"),
            (_, _) =>
            {
                successfulCalls++;
                return Task.CompletedTask;
            });
        var publisher = bus.CreatePublisher<PublisherMessage>(Topic("status"));

        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await publisher.PublishAsync(new PublisherMessage("ready", 1)));

        Assert.Equal(1, successfulCalls);
        Assert.Contains(exception.InnerExceptions, static inner =>
            inner is InvalidOperationException { Message: "subscriber failed" });
    }

    [Fact]
    public async Task Subscribe_ExposesPayloadAndConvenienceDeserialization()
    {
        await using var host = CreateHost();
        var hub = host.GetRequiredService<PluginEventHub>();
        await using var bus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.example", "1.0.0"),
            hub);
        bus.Resume();
        QuantumEvent? received = null;
        await using var subscription = bus.Subscribe(
            Topic("device.status.changed"),
            (@event, _) =>
            {
                received = @event;
                return Task.CompletedTask;
            });

        await bus.CreatePublisher<PublisherMessage>(Topic("device.status.changed"))
            .PublishAsync(new PublisherMessage("ready", 42));

        Assert.NotNull(received);
        Assert.Equal("ready", received.Payload.GetProperty("state").GetString());
        Assert.Equal(42, received.Payload.GetProperty("sequence").GetInt32());
        Assert.Equal(new SubscriberMessage("ready", 42),
            received.DeserializeRequired<SubscriberMessage>());
        Assert.Equal(new SubscriberMessage("ready", 42),
            received.Deserialize(typeof(SubscriberMessage)));
        Assert.True(received.TryDeserialize<SubscriberMessage>(out var deserialized));
        Assert.Equal(new SubscriberMessage("ready", 42), deserialized);
    }

    [Fact]
    public void AddQuantumPluginEventBus_IsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddQuantumPluginEventBus();
        services.AddQuantumPluginEventBus();

        Assert.Single(services, static descriptor =>
            descriptor.ServiceType == typeof(PluginEventHub));
        Assert.Single(services, static descriptor =>
            descriptor.ServiceType == typeof(PluginEventTransportHandler));
        Assert.Single(services, static descriptor =>
            descriptor.ServiceType == typeof(QuantumPluginEventBusFactory));
    }

    [Fact]
    public async Task ExternalRuntimeFactory_CreatesAnActiveOwnedEventBus()
    {
        await using var host = CreateHost();
        var factory = host.GetRequiredService<QuantumPluginEventBusFactory>();
        await using var bus = factory.Create(
            new QuantumPluginInfo("quantum.plugin.web", "1.0.0"));
        QuantumEvent? received = null;
        await using var subscription = bus.Subscribe(
            Topic("web.status"),
            (@event, _) =>
            {
                received = @event;
                return Task.CompletedTask;
            });

        await bus.CreatePublisher<PublisherMessage>(Topic("web.status"))
            .PublishAsync(new PublisherMessage("ready", 1));

        Assert.NotNull(received);
        Assert.Equal("quantum.plugin.web", received.Publisher.Id);
    }

    [Fact]
    public void CreatePublisher_RejectsDefaultTopicValue()
    {
        using var host = CreateHost();
        using var bus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.example", "1.0.0"),
            host.GetRequiredService<PluginEventHub>());

#pragma warning disable NOF018 // Intentionally forge the invalid value to verify the runtime guard.
        Assert.Throws<InvalidOperationException>(() => bus.CreatePublisher<PublisherMessage>(default));
#pragma warning restore NOF018
    }

    [Fact]
    public void Dispose_DoesNotKeepPublishedCollectibleMessageTypeAlive()
    {
        var references = PublishCollectibleMessage();

        for (var attempt = 0; attempt < 10 && references.LoadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(references.Bus.IsAlive, "The disposed plugin event bus is still rooted.");
        Assert.False(references.LoadContext.IsAlive, "The collectible plugin load context is still rooted.");
    }

    private static ServiceProvider CreateHost()
        => new ServiceCollection()
            .AddQuantumPluginEventBus()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CollectibleReferences PublishCollectibleMessage()
    {
        using var host = CreateHost();
        var loadContext = new PluginLoadContext(
            Path.Combine(AppContext.BaseDirectory, "Quantum.ExamplePlugin.dll"));
        var loadContextReference = new WeakReference(loadContext, trackResurrection: false);
        var messageType = loadContext
            .LoadEntryAssembly()
            .GetType("Quantum.ExamplePlugin.ExamplePluginHandshake", throwOnError: true)!;
        var message = Activator.CreateInstance(
            messageType,
            "collectible",
            1,
            DateTimeOffset.UtcNow,
            true)!;
        var bus = new PluginEventBus(
            new QuantumPluginInfo("quantum.plugin.collectible", "1.0.0"),
            host.GetRequiredService<PluginEventHub>());
        var busReference = new WeakReference(bus, trackResurrection: false);
        using (bus)
        {
            bus.Resume();
            using var subscription = bus.Subscribe(
                Topic("collectible.message"),
                (@event, _) =>
                {
                    Assert.NotNull(@event.Deserialize(messageType));
                    return Task.CompletedTask;
                });
            bus.CreatePublisher<object>(Topic("collectible.message"))
                .PublishAsync(message)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        loadContext.Unload();
        return new CollectibleReferences(loadContextReference, busReference);
    }

    private sealed record PublisherMessage(string State, int Sequence);

    private static QuantumTopic Topic(string value) => QuantumTopic.Of(value);

    private sealed record SubscriberMessage(string State, int Sequence);

    private sealed record CollectibleReferences(WeakReference LoadContext, WeakReference Bus);
}
