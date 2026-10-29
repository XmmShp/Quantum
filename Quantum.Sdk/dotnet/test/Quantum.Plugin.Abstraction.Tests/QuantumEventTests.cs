using System.Text.Json;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

public sealed class QuantumEventTests
{
    [Fact]
    public void Deserialize_UsesWebDefaultsAndSupportsGenericAndRuntimeTypes()
    {
        var @event = CreateEvent("""{"state":"ready","sequence":7}""");

        var generic = @event.Deserialize<PayloadMessage>();
        var runtime = @event.Deserialize(typeof(PayloadMessage));

        Assert.Equal(new PayloadMessage("ready", 7), generic);
        Assert.Equal(new PayloadMessage("ready", 7), runtime);
    }

    [Fact]
    public void DeserializeRequired_RejectsJsonNull()
    {
        var @event = CreateEvent("null");

        Assert.Null(@event.Deserialize<PayloadMessage>());
        Assert.Throws<JsonException>(() =>
            @event.DeserializeRequired<PayloadMessage>());
    }

    [Fact]
    public void TryDeserialize_ReturnsFalseForAnIncompatiblePayload()
    {
        var @event = CreateEvent("""{"state":"ready","sequence":"invalid"}""");

        var succeeded = @event.TryDeserialize<PayloadMessage>(out var message);

        Assert.False(succeeded);
        Assert.Null(message);
    }

    private static QuantumEvent CreateEvent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return new QuantumEvent(
            Guid.NewGuid(),
            QuantumTopic.Of("devices.status"),
            document.RootElement.Clone(),
            new QuantumPluginInfo("quantum.plugin.publisher", "1.0.0"),
            DateTimeOffset.UtcNow);
    }

    private sealed record PayloadMessage(string State, int Sequence);
}
