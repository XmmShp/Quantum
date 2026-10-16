using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quantum.Platform.Tests;

public sealed class CliPublishTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public async Task Publish_UsesPrivateKeyUploadAndExistingRelease(bool existing, bool changed, int exitCode)
    {
        var root = Path.Combine(Path.GetTempPath(), "quantum-cli-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "plugin");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "plugin.json"), """{"id":"quantum.plugin.calendar","version":"1.1.1"}""");
            var keyFile = Path.Combine(root, "private.json");
            using var rsa = RSA.Create(2048);
            var key = rsa.ExportParameters(true);
            string B(byte[]? bytes) => Convert.ToBase64String(bytes!).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            await File.WriteAllTextAsync(keyFile, JsonSerializer.Serialize(new { keys = new[] { new { kid = "test", kty = "RSA", n = B(key.Modulus), e = B(key.Exponent), d = B(key.D), p = B(key.P), q = B(key.Q), dp = B(key.DP), dq = B(key.DQ), qi = B(key.InverseQ) } } }));
            var createArchive = typeof(QuantumCli).GetMethod("CreateArchive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var archive = (byte[])createArchive.Invoke(null, [source])!;
            var sha = Convert.ToHexStringLower(SHA256.HashData(archive));
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            using var server = new HttpListener();
            var url = $"http://127.0.0.1:{port}/";
            server.Prefixes.Add(url);
            server.Start();
            var requests = new List<string>();
            var serve = Task.Run(async () =>
            {
                var count = existing ? 2 : 4;
                for (var i = 0; i < count; i++)
                {
                    var context = await server.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                    object response;
                    if (i == 0)
                    {
                        Assert.Contains("grant_type=client_credentials", body);
                        Assert.Contains("client_assertion=", body);
                        Assert.Contains("scope=plugin%3Apublish", body);
                        response = new { access_token = "test-token" };
                    }
                    else
                    {
                        Assert.Equal("Bearer test-token", context.Request.Headers["Authorization"]);
                        using var json = JsonDocument.Parse(body);
                        var method = json.RootElement.GetProperty("method").GetString()!;
                        requests.Add(method);
                        var release = new { releaseId = "42", version = "1.1.1", packageSha256 = changed ? "different" : sha, quantumVersionSupport = ">=0.1.0", status = 2, automatedReviewStatus = 3 };
                        if (method == "UploadPluginRelease")
                        {
                            var parameters = json.RootElement.GetProperty("params");
                            Assert.Equal(sha, parameters.GetProperty("expectedSha256").GetString());
                            Assert.Equal(archive, Convert.FromBase64String(parameters.GetProperty("packageArchiveBase64").GetString()!));
                            response = new { result = new { isSuccess = true, value = release with { status = 1, automatedReviewStatus = 1 } } };
                        }
                        else
                            response = new { result = new { isSuccess = true, value = existing || i == 3 ? new[] { release } : [] } };
                    }
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
            var actual = await QuantumCli.RunAsync(["plugins", "publish", source, "--quantum-version-support", ">=0.1.0", "--base-url", url, "--client-id", "test-ci", "--client-private-jwks", keyFile, "--wait-seconds", "1", "--output", "subprocess"]);
            await serve;
            Assert.Equal(exitCode, actual);
            Assert.Equal(existing ? new[] { "ListPluginReleases" } : new[] { "ListPluginReleases", "UploadPluginRelease", "ListPluginReleases" }, requests);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
