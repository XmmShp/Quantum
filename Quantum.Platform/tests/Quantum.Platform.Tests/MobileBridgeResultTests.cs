using System.Text.Json;
using NOF.Contract;
using Quantum.Platform.Contract;
using Xunit;

namespace Quantum.Platform.Tests;

public sealed class MobileBridgeResultTests
{
    [Theory]
    [InlineData("authentication_required")]
    [InlineData("mobile_invalid_request")]
    public void FailureCanBeSerializedByTheRpcTransport(string code)
    {
        Assert.Equal(typeof(Result<JsonElement?>), typeof(IMobileBridgeService)
            .GetMethod(nameof(IMobileBridgeService.ExchangeMobileBridge))!.ReturnType);
        Result<JsonElement?> result = Result.Fail(code, "Request rejected.");
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(code, json.GetProperty("errorCode").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("value").ValueKind);
    }

    [Fact]
    public void SuccessRetainsTheExistingBridgePayloadShape()
    {
        var payload = JsonSerializer.SerializeToElement(new { devices = Array.Empty<object>() });
        var result = Result.Success<JsonElement?>(payload);
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(0, json.GetProperty("value").GetProperty("devices").GetArrayLength());
    }
}
