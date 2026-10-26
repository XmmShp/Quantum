using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Quantum.MobileApp.Debugging;

namespace Quantum.Tests;

public sealed class MobileDebugServerTests
{
    private const string Token = "fixture-debug-token-0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("contains spaces token 0123456789abcdef")]
    public void InvalidTokensCannotStartTheServer(string? token)
        => Assert.ThrowsAny<ArgumentException>(() => new MobileDebugServer(new() { Port = FreePort(), Token = token ?? "" }, new Target()));

    [Fact]
    public async Task RequestsRequireThePerLaunchCredentialAndRejectBrowserOrigins()
    {
        var target = new Target();
        await using var server = Start(target);
        using var http = Client(server);
        using var unauthenticated = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        http.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        using var valid = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "state");
        request.Headers.Add("Origin", "https://untrusted.example");
        using var origin = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public async Task NoArbitraryRpcAuthenticationOrAuthorizationRoutesExist()
    {
        var target = new Target();
        await using var server = Start(target);
        using var http = Client(server);
        http.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        foreach (var path in new[] { "rpc", "login", "approve", "revoke", "eval", "state?token=" + Token })
        {
            using var response = await http.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(0, target.Calls);
    }

    [Fact]
    public async Task JsonCommandsAreDispatchedOnceAndErrorsNeverExposeExceptionDetails()
    {
        var target = new Target();
        await using var server = Start(target);
        using var http = Client(server);
        http.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        using var input = await http.PostAsync("input", new StringContent("{\"nodeId\":\"title\",\"value\":\"测试\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, input.StatusCode);
        Assert.Equal("测试", target.LastBody.GetProperty("value").GetString());
        target.Failure = new InvalidOperationException("private-account-token");
        using var failed = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("private-account-token", await failed.Content.ReadAsStringAsync());
        target.Failure = new MobileDebugException(409, "debug_stale_view");
        using var stale = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(3, target.Calls);
    }

    [Theory]
    [InlineData("Host: attacker.example\r\n", 403)]
    [InlineData("Content-Length: 0\r\nContent-Length: 0\r\n", 400)]
    [InlineData("Transfer-Encoding: chunked\r\n", 400)]
    [InlineData("Content-Length: 32769\r\n", 413)]
    public async Task RebindingAmbiguousFramingAndOversizedBodiesAreRejected(string extraHeaders, int expected)
    {
        var target = new Target();
        await using var server = Start(target);
        var host = extraHeaders.StartsWith("Host:") ? "" : $"Host: 127.0.0.1:{server.Port}\r\n";
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.Port);
        var bytes = Encoding.ASCII.GetBytes($"GET /state HTTP/1.1\r\n{host}Authorization: Bearer {Token}\r\n{extraHeaders}\r\n");
        await tcp.GetStream().WriteAsync(bytes);
        using var reader = new StreamReader(tcp.GetStream());
        var response = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.StartsWith($"HTTP/1.1 {expected} ", response);
        Assert.Equal(0, target.Calls);
    }

    [Fact]
    public async Task NestedUiTreesAndUnsupportedResponsesDoNotStopTheServer()
    {
        var target = new Target();
        object nested = "leaf";
        for (var i = 0; i < 20; i++) nested = new Dictionary<string, object> { ["children"] = new[] { nested } };
        target.Value = nested;
        await using var server = Start(target);
        using var http = Client(server);
        http.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        using var tree = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.OK, tree.StatusCode);
        Assert.Contains("leaf", await tree.Content.ReadAsStringAsync());
        target.Value = typeof(MobileDebugServer);
        using var unsupported = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.InternalServerError, unsupported.StatusCode);
        target.Value = null;
        using var healthy = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Fact]
    public async Task MalformedJsonDoesNotKillTheServerAndDisposeClosesThePort()
    {
        var target = new Target();
        var server = Start(target);
        using var http = Client(server);
        http.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        using var bad = await http.PostAsync("input", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var healthy = await http.GetAsync("state");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        await server.DisposeAsync();
        using var tcp = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(async () => await tcp.ConnectAsync(IPAddress.Loopback, server.Port));
    }

    private static MobileDebugServer Start(Target target) => new(new() { Port = FreePort(), Token = Token }, target);
    private static HttpClient Client(MobileDebugServer server) => new(new HttpClientHandler { UseProxy = false })
    { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}/"), Timeout = TimeSpan.FromSeconds(5) };
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    private sealed class Target : IMobileDebugTarget
    {
        public int Calls { get; private set; }
        public JsonElement LastBody { get; private set; }
        public Exception? Failure { get; set; }
        public object? Value { get; set; }
        public Task<object> ExecuteAsync(string path, JsonElement body)
        {
            Calls++; LastBody = body;
            return Failure is { } failure ? Task.FromException<object>(failure) : Task.FromResult(Value ?? (object)new { path });
        }
    }
}
