using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantum.Marketplace;

namespace Quantum.Tests;

public sealed class QuantumMarketplaceClientTests
{
    [Fact]
    public async Task ListPluginsAsync_UsesJsonRpcEnvelopeAndReadsNofResult()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {
                  "jsonrpc": "2.0",
                  "id": "response",
                  "result": {
                    "isSuccess": true,
                    "value": [
                      {
                        "pluginId": "quantum.plugin.calendar",
                        "name": "Calendar",
                        "description": "Plans",
                        "authorName": "Quantum",
                        "tags": ["official"],
                        "latestRelease": {
                          "version": "1.0.0",
                          "quantumVersionSupport": ">=0.1.0",
                          "releaseNotes": "First release",
                          "status": 2,
                          "packageSizeBytes": 128,
                          "packageSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                          "uploadedAtUtc": "2026-09-14T00:00:00Z",
                          "downloadCount": 3
                        }
                      }
                    ]
                  }
                }
                """);
        });
        var client = CreateClient(handler);

        var plugins = await client.ListPluginsAsync(" calendar ", ["official"]);

        var plugin = Assert.Single(plugins);
        Assert.Equal("quantum.plugin.calendar", plugin.PluginId);
        Assert.Equal("1.0.0", plugin.LatestRelease!.Version);
        using var request = JsonDocument.Parse(requestBody!);
        Assert.Equal("ListPlugins", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("calendar", request.RootElement.GetProperty("params").GetProperty("search").GetString());
    }

    [Fact]
    public async Task InvokeAsync_MapsFailedNofResultToMarketplaceException()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse("""
            {
              "jsonrpc": "2.0",
              "id": "response",
              "result": {
                "isSuccess": false,
                "errorCode": "plugin_not_found",
                "message": "The plugin was not found."
              }
            }
            """)));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MarketplaceException>(
            () => client.GetPluginAsync("quantum.plugin.missing"));

        Assert.Equal("The plugin was not found.", exception.Message);
    }

    [Fact]
    public async Task DownloadAsync_VerifiesSizeAndSha256()
    {
        var archive = Encoding.UTF8.GetBytes("plugin archive");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(archive));
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse($$"""
            {
              "jsonrpc": "2.0",
              "id": "response",
              "result": {
                "isSuccess": true,
                "value": {
                  "fileName": "calendar.zip",
                  "contentType": "application/zip",
                  "packageArchiveBase64": "{{Convert.ToBase64String(archive)}}",
                  "packageSizeBytes": {{archive.Length}},
                  "packageSha256": "{{sha256}}"
                }
              }
            }
            """)));
        var client = CreateClient(handler);

        var download = await client.DownloadAsync("quantum.plugin.calendar", "1.0.0");

        Assert.Equal(archive, download.Archive);
        Assert.Equal(sha256, download.PackageSha256);
    }

    [Fact]
    public async Task DownloadAsync_RejectsChecksumMismatch()
    {
        var archive = Encoding.UTF8.GetBytes("plugin archive");
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse($$"""
            {
              "jsonrpc": "2.0",
              "id": "response",
              "result": {
                "isSuccess": true,
                "value": {
                  "fileName": "calendar.zip",
                  "contentType": "application/zip",
                  "packageArchiveBase64": "{{Convert.ToBase64String(archive)}}",
                  "packageSizeBytes": {{archive.Length}},
                  "packageSha256": "{{new string('0', 64)}}"
                }
              }
            }
            """)));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MarketplaceException>(
            () => client.DownloadAsync("quantum.plugin.calendar", "1.0.0"));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static QuantumMarketplaceClient CreateClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new QuantumMarketplaceOptions(new Uri("https://market.example/"), "0.1.0"));

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request);
    }
}
