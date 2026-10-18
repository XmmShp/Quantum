using Microsoft.Extensions.DependencyInjection;
using NOF.Contract;
using NOF.Hosting;
using Quantum.OfficialPlugins.Codex;
using Quantum.OfficialPlugins.Codex.Application;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class CodexRpcTests
{
    private static OfficialRpcHarness Host(ICodexIntegrationService? fake = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNOFApplication();
        services.AddSingleton<IRpcInvoker, UnavailableInvoker>();
        OfficialPlugins.Codex.CodexPlugin.ConfigureServices(services);
        if (fake is not null) services.AddSingleton(fake);
        return new(services, "quantum.plugin.codex", typeof(ICodexRpcService).Assembly);
    }

    [Fact]
    public async Task ProductionHandlersAreDiscoverableAndReadOnlyCallsNeedNoAppServer()
    {
        await using var host = Host();
        await host.AssertCatalog("quantum.plugin.codex", "codex", ["snapshot", "models", "connect", "new-thread", "send", "disconnect"]);
        var snapshot = OfficialRpcHarness.Value(await host.Call("quantum.plugin.codex.codex.snapshot", new { }));
        Assert.Equal((int)CodexConnectionState.Disconnected, snapshot.GetProperty("connectionState").GetInt32());
        Assert.NotEmpty(OfficialRpcHarness.Value(await host.Call("codex.models", new { })).EnumerateArray());
        OfficialRpcHarness.Value(await host.Call("codex.disconnect", new { }));
        var invalidState = await host.Call("codex.send", new CodexSendRequest("test"));
        Assert.Equal("codex_invalid_state", invalidState.GetProperty("errorCode").GetString());
        var invalidDirectory = await host.Call("codex.connect", new CodexConnectRequest(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.Equal("codex_connection_failed", invalidDirectory.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task HostDispatchRoutesAllCommandsToTheSharedApplicationService()
    {
        var fake = new FakeCodex();
        await using var host = Host(fake);
        OfficialRpcHarness.Value(await host.Call("codex.connect", new CodexConnectRequest("workspace", "model-a")));
        Assert.Equal("workspace", fake.Snapshot.WorkingDirectory);
        var thread = OfficialRpcHarness.Value(await host.Call("codex.new-thread", new CodexNewThreadRequest("model-b")));
        Assert.Equal("thread-2", thread.GetProperty("threadId").GetString());
        var send = OfficialRpcHarness.Value(await host.Call("codex.send", new CodexSendRequest("Test prompt")));
        Assert.True(send.GetProperty("isBusy").GetBoolean());
        Assert.Equal("Test prompt", fake.LastPrompt);
        OfficialRpcHarness.Value(await host.Call("codex.snapshot", new { }));
        OfficialRpcHarness.Value(await host.Call("codex.models", new { }));
        OfficialRpcHarness.Value(await host.Call("codex.disconnect", new { }));
        Assert.Equal(CodexConnectionState.Disconnected, fake.Snapshot.ConnectionState);
        Assert.Equal(new[] { "connect", "new-thread", "send", "disconnect" }, fake.Commands);
    }

    [Fact]
    public async Task InvalidRequestsFailedConnectionsAndCancellationAreReported()
    {
        var fake = new FakeCodex { FailConnect = true };
        await using var host = Host(fake);
        var invalidPrompt = await host.Call("codex.send", new CodexSendRequest(" "));
        Assert.Equal("codex_invalid_request", invalidPrompt.GetProperty("errorCode").GetString());
        var failed = await host.Call("codex.connect", new CodexConnectRequest("workspace", "model-a"));
        Assert.Equal("codex_connection_failed", failed.GetProperty("errorCode").GetString());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("codex.send", new CodexSendRequest("test"), cancellationToken: cancelled.Token));
        Assert.Null(fake.LastPrompt);
    }

    private sealed class UnavailableInvoker : IRpcInvoker
    {
        public Task<Result<T>> InvokeAsync<T>(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<T>>(Result.Fail("not_available", "Test has no external RPC services."));
        public Task<Result> InvokeAsync(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => Task.FromResult<Result>(Result.Fail("not_available", "Test has no external RPC services."));
    }

    private sealed class FakeCodex : ICodexIntegrationService
    {
        public event Action? Changed { add { } remove { } }
        public Task EnsureAutoConnectedAsync() => Task.CompletedTask;
        public bool FailConnect { get; init; }
        public string? LastPrompt { get; private set; }
        public List<string> Commands { get; } = [];
        public CodexIntegrationSnapshot Snapshot { get; private set; } = new(CodexConnectionState.Disconnected, null, false, 0,
            "", [new("model-a", "A"), new("model-b", "B")], "model-a", [], null);

        public Task ConnectAsync(string workingDirectory, string model, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Commands.Add("connect");
            Snapshot = Snapshot with { ConnectionState = FailConnect ? CodexConnectionState.Failed : CodexConnectionState.Connected,
                WorkingDirectory = workingDirectory, SelectedModel = model, ThreadId = "thread-1", ErrorMessage = FailConnect ? "Failed" : null };
            return Task.CompletedTask;
        }
        public Task StartNewThreadAsync(string? model = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Commands.Add("new-thread");
            Snapshot = Snapshot with { ThreadId = "thread-2", SelectedModel = model ?? Snapshot.SelectedModel }; return Task.CompletedTask;
        }
        public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Commands.Add("send"); LastPrompt = prompt;
            Snapshot = Snapshot with { IsBusy = true }; return Task.CompletedTask;
        }
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Commands.Add("disconnect");
            Snapshot = Snapshot with { ConnectionState = CodexConnectionState.Disconnected, ThreadId = null, IsBusy = false }; return Task.CompletedTask;
        }
    }
}
