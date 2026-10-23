using MobilePage = Quantum.Plugin.Abstraction.MobilePage;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using NOF.Contract;
using Quantum.Mobile;
using Quantum.Mobile.Internal;
using Quantum.Plugins;
using MobileUi = Quantum.Plugin.Abstraction.Mobile;

namespace Quantum.Tests;

public sealed class MobileActionGatewayTests
{
    [Fact]
    public void InternalProtocolIsIndependentOfPluginsAndRejectsExecutableWireActions()
    {
        Assert.DoesNotContain(typeof(BridgeClient).Assembly.GetReferencedAssemblies(), a =>
            a.Name!.StartsWith("NOF") || a.Name.Contains("Plugin"));
        var page = Quantum.Mobile.Internal.Mobile.Page("p", "Page",
            Quantum.Mobile.Internal.Mobile.Button("save", "Save").OnClick(
                Quantum.Mobile.Internal.Mobile.Rpc("plugin.notes.create")));
        Assert.Throws<ArgumentException>(() => Quantum.Mobile.Internal.MobileUiValidator.Validate(page, wire: true));
    }

    [Fact]
    public async Task FixedActionUsesRealPluginRpcWithProtectedCallerAndInputs()
    {
        await using var fixture = new Fixture();
        var snapshot = fixture.Open();
        var button = snapshot.Page.Root.Children[1];
        Assert.Null(button.Action);
        Assert.DoesNotContain("notebookId", MobileJson.Element(snapshot).GetRawText());
        var outcome = await fixture.Gateway.InvokeAsync("request", "phone", new(snapshot.ViewId, button.ActionId!, new() { ["text"] = MobileJson.Element("Hello") }), default);
        Assert.Equal("succeeded", outcome.Status);
        var result = outcome.Result!.Value.GetProperty("value");
        Assert.Equal("inbox:Hello:quantum.plugin.mobile:phone", result.GetString());
        Assert.Equal(1, fixture.Counter.Calls);
    }
    [Fact]
    public async Task TamperedInputsOtherDevicesAndExpiredPagesCannotInvokeRpc()
    {
        await using var fixture = new Fixture();
        var snapshot = fixture.Open(); var actionId = snapshot.Page.Root.Children[1].ActionId!;
        var valid = new BridgeInvoke(snapshot.ViewId, actionId, new() { ["text"] = MobileJson.Element("Hello") });
        await Assert.ThrowsAsync<BridgeException>(() => fixture.Gateway.InvokeAsync("r", "other-phone", valid, default));
        await Assert.ThrowsAsync<BridgeException>(() => fixture.Gateway.InvokeAsync("r", "phone", valid with { Inputs = new() { ["text"] = MobileJson.Element("Hello"), ["notebookId"] = MobileJson.Element("secret") } }, default));
        await Assert.ThrowsAsync<BridgeException>(() => fixture.Gateway.InvokeAsync("r", "phone", valid with { Inputs = new() { ["text"] = MobileJson.Element(new string('x', 501)) } }, default));
        fixture.Gateway.SetSharing([]);
        await Assert.ThrowsAsync<BridgeException>(() => fixture.Gateway.InvokeAsync("r", "phone", valid, default));
        Assert.Equal(0, fixture.Counter.Calls);
    }
    [Fact]
    public async Task PageReplacementAndRuntimeStopInvalidateOldActions()
    {
        await using var fixture = new Fixture();
        var snapshot = fixture.Open(); var button = snapshot.Page.Root.Children[1];
        fixture.Plugin.MobilePages.Publish(fixture.Page);
        await Assert.ThrowsAsync<BridgeException>(() => fixture.Gateway.InvokeAsync("r", "phone", new(snapshot.ViewId, button.ActionId!, new() { ["text"] = MobileJson.Element("Hello") }), default));
        await fixture.Runtime.PauseAsync(default);
        Assert.Empty(fixture.Gateway.Pages());
        Assert.Throws<BridgeException>(() => fixture.Open());
        Assert.Equal(0, fixture.Counter.Calls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly PluginRpcSerializer _serializer = new();
        private readonly PluginRpcRouter _router;
        public MobileTestCounter Counter { get; } = new();
        public PluginRpcRuntime Runtime { get; }
        public LoadedPlugin Plugin { get; }
        public MobileActionGateway Gateway { get; }
        public MobilePage Page { get; } = MobileUi.Page("notes", "Notes", MobileUi.VStack("root", MobileUi.TextField("text"),
            MobileUi.Button("save", "Save").OnClick(MobileUi.Rpc("quantum.plugin.mobile.mobile-test.save").Constant("notebookId", "inbox").Input("text", "text"))));
        public Fixture()
        {
            _services = new ServiceCollection().AddQuantumPluginRpc().AddSingleton(Counter).AddScoped<MobileTestServer.Save, MobileTestSave>().BuildServiceProvider();
            var id = Guid.NewGuid();
            Runtime = PluginRpcRuntime.Create(PluginId.Of("quantum.plugin.mobile"), id, typeof(MobileTestServer).Assembly,
                _services.GetRequiredService<IServiceScopeFactory>(), _serializer, NullLogger.Instance);
            Runtime.Resume();
            Plugin = new(new PluginManifest(PluginId.Of("quantum.plugin.mobile"), SemanticVersion.Of("1.0.0"), "Plugin.dll"),
                ".", typeof(MobileTestServer).Assembly, [], id, _services)
            { RpcRuntime = Runtime };
            Plugin.MobilePages.Publish(Page);
            var catalog = new PluginCatalog([Plugin]);
            _router = new(catalog, NullLogger<PluginRpcRouter>.Instance);
            Gateway = new(catalog, _router); Gateway.SetSharing(["quantum.plugin.mobile"]);
        }
        public BridgeSnapshot Open(DateTimeOffset? now = null) => Gateway.Open("phone", "desktop-session", new("quantum.plugin.mobile", "notes"), now ?? DateTimeOffset.UtcNow);
        public async ValueTask DisposeAsync() { _router.Dispose(); await Runtime.DisposeAsync(); _serializer.Dispose(); await _services.DisposeAsync(); }
    }
}

public sealed class MobileTestCounter { public int Calls { get; set; } }
public sealed record MobileTestRequest(string Text, string NotebookId);
[TransportOverQuantum]
[RpcInvocationName("mobile-test")]
public interface IMobileTestService : IRpcService { Result<string> Save(MobileTestRequest request); }
public partial class MobileTestServer : RpcServer<IMobileTestService>;
public sealed class MobileTestSave(MobileTestCounter counter) : MobileTestServer.Save
{
    public override Task<Result<string>> HandleAsync(MobileTestRequest request, Context context, CancellationToken cancellationToken)
    {
        counter.Calls++;
        return Task.FromResult<Result<string>>($"{request.NotebookId}:{request.Text}:{context[QuantumRpcContextKeys.CallerPluginId]}:{context["quantum.mobile.deviceId"]}");
    }
}
