using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quantum.Marketplace;
using Quantum.Mobile;
using Quantum.Mobile.Internal;
using Quantum.Platform.Application;
using Quantum.Plugins;
using MobileUi = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.Tests;

public sealed class MobileBridgeEndToEndTests
{
    [Fact]
    public async Task AuthenticatedPollingInvokesRealRpcOnceAndPersistsOutcome()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        using var http = new HttpClient(new PlatformHandler());
        var options = new QuantumMarketplaceOptions(new Uri("https://quantum.test/"), "0.1.0");
        var session = new QuantumMarketplaceSession(() => Task.FromResult<string?>(null), _ => Task.CompletedTask, () => Task.CompletedTask);
        var marketplace = new QuantumMarketplaceClient(http, options, session);
        await session.LoginAsync(marketplace, "test@example.test", "test-password", token);
        var counter = new MobileTestCounter();
        await using var services = new ServiceCollection().AddQuantumPluginRpc().AddSingleton(counter).AddScoped<MobileTestServer.Save, MobileTestSave>().BuildServiceProvider();
        using var serializer = new PluginRpcSerializer();
        var runtimeId = Guid.NewGuid();
        await using var runtime = PluginRpcRuntime.Create(PluginId.Of("quantum.plugin.mobile"), runtimeId, typeof(MobileTestServer).Assembly,
            services.GetRequiredService<IServiceScopeFactory>(), serializer, NullLogger.Instance);
        runtime.Resume();
        var plugin = new LoadedPlugin(new PluginManifest(PluginId.Of("quantum.plugin.mobile"), SemanticVersion.Of("1.0.0"), "Plugin.dll"), ".", typeof(MobileTestServer).Assembly, [], runtimeId, services) { RpcRuntime = runtime };
        plugin.MobilePages.Publish(MobileUi.Page("notes", "Notes", MobileUi.VStack("root", MobileUi.TextField("text"),
            MobileUi.Button("save", "Save").OnClick(MobileUi.Rpc("quantum.plugin.mobile.mobile-test.save").Constant("notebookId", "inbox").Input("text", "text")))));
        var catalog = new PluginCatalog([plugin]);
        using var router = new PluginRpcRouter(catalog, NullLogger<PluginRpcRouter>.Instance);
        var gateway = new MobileActionGateway(catalog, router);
        var journal = Path.Combine(Path.GetTempPath(), "quantum-mobile-test-" + Guid.NewGuid().ToString("N"));
        string? registration = null;
        await using var desktop = new MobileDesktopBridge(http, session, options, gateway, journal,
            () => Task.FromResult(registration), text => { registration = text; return Task.CompletedTask; }, () => registration = null);
        try
        {
            await desktop.StartAsync(["quantum.plugin.mobile"], token);
            var phone = new BridgeClient(http, options.BaseAddress, () => session.AccessToken);
            phone.Registration = await phone.CallAsync<BridgeRegistration>("register", new { name = "Phone", kind = "mobile" }, token);
            var device = (await phone.CallAsync<BridgeDevice[]>("devices", cancellationToken: token)).Single(d => d.Kind == "desktop");
            await phone.CallAsync<JsonElement>("bind", new { desktopId = device.Id }, token);
            await desktop.ApproveAsync(phone.Registration.Device.Id, true);
            await phone.CallAsync<JsonElement>("send", new BridgeSend("open", device.Id, "open", MobileJson.Element(new BridgeOpen("quantum.plugin.mobile", "notes"))), token);
            BridgeSnapshot? snapshot = null;
            while (snapshot is null)
            {
                foreach (var message in await phone.CallAsync<BridgeMessage[]>("poll", cancellationToken: token))
                {
                    if (message.Kind == "snapshot") snapshot = MobileJson.Read<BridgeSnapshot>(message.Body);
                    await phone.CallAsync<JsonElement>("ack", new BridgeAck(message.Id, message.Lease!), token);
                }
                if (snapshot is null) await Task.Delay(50, token);
            }
            var command = new BridgeSend("e2e-action", device.Id, "action", MobileJson.Element(new BridgeInvoke(snapshot.ViewId,
                snapshot.Page.Root.Children[1].ActionId!, new() { ["text"] = MobileJson.Element("from-phone") })));
            await phone.CallAsync<JsonElement>("send", command, token);
            await phone.CallAsync<JsonElement>("send", command, token); // Response-loss retry uses exactly the same ID and payload.
            BridgeOutcome outcome;
            do
            {
                await Task.Delay(50, token);
                outcome = await phone.CallAsync<BridgeOutcome>("status", new { requestId = command.Id }, token);
            } while (outcome.Status is "accepted" or "running");
            Assert.Equal("succeeded", outcome.Status);
            Assert.Equal(1, counter.Calls);
            Assert.Contains("inbox:from-phone:quantum.plugin.mobile:", outcome.Result!.Value.GetProperty("value").GetString());
            Assert.Single(Directory.GetFiles(journal, "*.json"));
            var saved = await File.ReadAllTextAsync(Directory.GetFiles(journal, "*.json")[0], token);
            Assert.Contains("succeeded", saved);
            // Simulate a durable Running claim recovered without a committed outcome.
            // The bridge must publish unknown, never execute that request again.
            const string recoveredId = "recovered-running";
            await File.WriteAllTextAsync(Path.Combine(journal, device.Id + "-" + recoveredId + ".json"),
                JsonSerializer.Serialize(new
                {
                    desktopId = device.Id,
                    mobileId = phone.Registration.Device.Id,
                    requestId = recoveredId,
                    createdAt = DateTimeOffset.UtcNow,
                    outcome = (BridgeOutcome?)null,
                    uploaded = false
                }, MobileJson.Options), token);
            await phone.CallAsync<JsonElement>("send", command with { Id = recoveredId }, token);
            do
            {
                await Task.Delay(50, token);
                outcome = await phone.CallAsync<BridgeOutcome>("status", new { requestId = recoveredId }, token);
            } while (outcome.Status is "accepted" or "running");
            Assert.Equal("unknown", outcome.Status);
            Assert.Equal(1, counter.Calls);
            // The phone cannot swap the RPC name: that is not part of its wire action contract.
            await Assert.ThrowsAsync<BridgeException>(() => phone.CallAsync<JsonElement>("send", command with
            {
                Body = MobileJson.Element(new { viewId = snapshot.ViewId, actionId = "other", inputs = new { text = "changed" }, rpcName = "arbitrary.rpc" })
            }, token));
        }
        finally
        {
            await desktop.StopAsync();
            // The unique temporary test directory is owned by this fixture.
            if (Directory.Exists(journal)) Directory.Delete(journal, recursive: true);
        }
    }

    private sealed class PlatformHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private MobileBridgeMailbox _mailbox = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            object result;
            lock (_gate)
            {
                if (root.GetProperty("method").GetString() == "Login")
                    result = new { isSuccess = true, value = new MarketplaceLogin("test-token", DateTime.UtcNow.AddHours(1), new("1", "tester", "test@example.test", MarketplaceUserRoles.User)) };
                else if (request.Headers.Authorization?.Parameter != "test-token")
                    result = new { isSuccess = false, errorCode = "authentication_required", message = "Sign in first." };
                else
                {
                    Assert.Equal("/rpc/mobile", request.RequestUri!.AbsolutePath);
                    try
                    {
                        var value = _mailbox.Exchange(MobileJson.Read<BridgeRequest>(root.GetProperty("params")), DateTimeOffset.UtcNow);
                        _mailbox = JsonSerializer.Deserialize<MobileBridgeMailbox>(JsonSerializer.Serialize(_mailbox, MobileJson.Options), MobileJson.Options)!;
                        result = new { isSuccess = true, value };
                    }
                    catch (BridgeException error) { result = new { isSuccess = false, errorCode = error.Code, message = error.Message }; }
                    catch (Exception error) when (error is ArgumentException or JsonException)
                    { result = new { isSuccess = false, errorCode = "mobile_invalid_request", message = "Invalid request" }; }
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = root.GetProperty("id").GetString(), result }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
        }
    }
}
