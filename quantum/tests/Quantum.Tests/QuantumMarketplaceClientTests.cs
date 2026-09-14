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

    [Fact]
    public async Task AuthenticatedRequest_UsesTokenSavedByEmailPasswordLogin()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("""
                {"jsonrpc":"2.0","id":"1","result":{"isSuccess":true,"value":{"accessToken":"secret-token","expiresAtUtc":"2026-09-15T00:00:00Z","user":{"userId":"1","username":"developer","email":"developer@example.com","roles":3}}}}
                """),
            JsonResponse("""
                {"jsonrpc":"2.0","id":"2","result":{"isSuccess":true,"value":[]}}
                """)
        ]);
        string? authorization = null;
        string? storedToken = null;
        var session = new QuantumMarketplaceSession(
            () => Task.FromResult<string?>(storedToken),
            token => { storedToken = token; return Task.CompletedTask; },
            () => { storedToken = null; return Task.CompletedTask; });
        var handler = new StubHttpMessageHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString() ?? authorization;
            return Task.FromResult(responses.Dequeue());
        });
        var client = new QuantumMarketplaceClient(
            new HttpClient(handler),
            new QuantumMarketplaceOptions(new Uri("https://market.example/"), "0.1.0"),
            session);

        await session.LoginAsync(client, "developer@example.com", "password");
        await client.ListManagedPluginsAsync();

        Assert.Equal("secret-token", storedToken);
        Assert.Equal("Bearer secret-token", authorization);
        Assert.Equal("developer@example.com", session.User!.Email);
    }

    [Fact]
    public async Task DownloadAsync_SendsSavedTokenForOwnedPendingRelease()
    {
        var archive = Encoding.UTF8.GetBytes("pending plugin archive");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(archive));
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("""
                {"jsonrpc":"2.0","id":"1","result":{"isSuccess":true,"value":{"accessToken":"pending-token","expiresAtUtc":"2026-09-15T00:00:00Z","user":{"userId":"1","username":"developer","email":"developer@example.com","roles":3}}}}
                """),
            JsonResponse($$"""
                {
                  "jsonrpc": "2.0",
                  "id": "2",
                  "result": {
                    "isSuccess": true,
                    "value": {
                      "fileName": "pending.zip",
                      "contentType": "application/zip",
                      "packageArchiveBase64": "{{Convert.ToBase64String(archive)}}",
                      "packageSizeBytes": {{archive.Length}},
                      "packageSha256": "{{sha256}}"
                    }
                  }
                }
                """)
        ]);
        string? authorization = null;
        var session = new QuantumMarketplaceSession(
            () => Task.FromResult<string?>(null),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Headers.Authorization is not null)
            {
                authorization = request.Headers.Authorization.ToString();
            }
            return Task.FromResult(responses.Dequeue());
        });
        var client = new QuantumMarketplaceClient(
            new HttpClient(handler),
            new QuantumMarketplaceOptions(new Uri("https://market.example/"), "0.1.0"),
            session);

        await session.LoginAsync(client, "developer@example.com", "password");
        await client.DownloadAsync("quantum.plugin.calendar", "1.1.0-beta.1");

        Assert.Equal("Bearer pending-token", authorization);
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
