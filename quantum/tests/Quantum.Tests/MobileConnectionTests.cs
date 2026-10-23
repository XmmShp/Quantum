using System.Net;
using System.Text;
using Quantum.Mobile.Internal;

namespace Quantum.Tests;

public sealed class MobileConnectionTests
{
    [Theory]
    [InlineData("https://quantum.io-vii.com", "https://quantum.io-vii.com/")]
    [InlineData(" https://example.com/platform/ ", "https://example.com/platform/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void AdvancedSettingsNormalizeValidPlatformAddresses(string input, string expected)
    {
        Assert.Equal(expected, PlatformAddress.Parse(input).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a URL")]
    [InlineData("http://example.com")]
    [InlineData("file:///C:/test")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com/?key=value")]
    [InlineData("https://example.com/#section")]
    [InlineData("https://example.com/portal/")]
    [InlineData("https://example.com/rpc")]
    public void AdvancedSettingsRejectInvalidEndpointsAndEmbeddedCredentials(string input)
    {
        Assert.Equal("invalid_platform_address", Assert.Throws<BridgeException>(() => PlatformAddress.Parse(input)).Code);
    }

    [Fact]
    public async Task BridgeAcceptsNullableRpcErrorMemberAndPreservesBusinessFailureCode()
    {
        using var http = new HttpClient(new ReplyHandler());
        var client = new BridgeClient(http, PlatformAddress.Parse(PlatformAddress.DefaultUrl), () => "fixture-token");
        Assert.Empty(await client.CallAsync<BridgeDevice[]>("devices"));
        var failure = await Assert.ThrowsAsync<BridgeException>(() => client.CallAsync<BridgeDevice[]>("devices"));
        Assert.Equal("mobile_forbidden", failure.Code);
    }

    private sealed class ReplyHandler : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/rpc/mobile", request.RequestUri!.AbsolutePath);
            var json = _calls++ == 0
                ? """{"jsonrpc":"2.0","id":"1","error":null,"result":{"isSuccess":true,"value":[]}}"""
                : """{"jsonrpc":"2.0","id":"2","error":null,"result":{"isSuccess":false,"errorCode":"mobile_forbidden","message":"Device not approved."}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
