using System.Text.Json;
using NOF.Domain;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

public sealed class QuantumTopicTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("devices.camera.status")]
    [InlineData("quantum-plugin.device_status.changed-v2")]
    public void Of_ReturnsCanonicalValueObject(string topic)
    {
        var value = QuantumTopic.Of(topic);

        Assert.Equal(topic, (string)value);
        Assert.Equal(topic, value.ToString());
        Assert.Equal(QuantumTopic.Of(topic), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".status")]
    [InlineData("status.")]
    [InlineData("devices..status")]
    [InlineData("1status")]
    [InlineData("_status")]
    [InlineData("devices._status")]
    [InlineData("devices.-status")]
    [InlineData("设备.status")]
    [InlineData(" status")]
    [InlineData("status ")]
    [InlineData("device status")]
    [InlineData("devices/status")]
    [InlineData("devices\\status")]
    [InlineData("devices.status?latest")]
    [InlineData("devices.status+")]
    [InlineData("status\n")]
    public void Of_RejectsInvalidTopic(string topic)
        => Assert.Throws<DomainValidationException>(() => QuantumTopic.Of(topic));

    [Fact]
    public void Of_RejectsTopicLongerThanDeclaredValueObjectLimit()
        => Assert.Throws<DomainValidationException>(() => QuantumTopic.Of(new string('a', 256)));

    [Fact]
    public void JsonConverter_RoundTripsAsUnderlyingString()
    {
        var topic = QuantumTopic.Of("devices.camera.status");

        var json = JsonSerializer.Serialize(topic);
        var restored = JsonSerializer.Deserialize<QuantumTopic>(json);

        Assert.Equal("\"devices.camera.status\"", json);
        Assert.Equal(topic, restored);
    }
}
