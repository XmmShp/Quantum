using Microsoft.Extensions.DependencyInjection;
using NOF.Contract;
using NOF.Hosting;
using Quantum.OfficialPlugins.Codex;
using Quantum.OfficialPlugins.Codex.Application;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class CodexRpcTests
{
    private static OfficialRpcHarness Host(ICodexRuntime? fake = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNOFApplication();
        services.AddSingleton<IRpcInvoker, UnavailableInvoker>();
        OfficialPlugins.Codex.CodexPlugin.ConfigureServices(services);
        if (fake is not null)
        {
            services.AddSingleton(fake);
        }

        return new(services, "quantum.plugin.codex", typeof(ICodexRpcService).Assembly);
    }

    [Fact]
    public async Task GeneratedLocalClientSharesRuntimeWithHostRpcAndExposesAutomaticConnection()
    {
        var fake = new FakeCodex();
        await using var host = Host(fake);
        await host.UseClient<ICodexRpcServiceClient>(async client =>
        {
            var context = Context.Empty;
            Assert.True((await client.EnsureAutoConnectedAsync(new(), context)).IsSuccess);
            Assert.Equal(1, fake.AutomaticCalls);
            OfficialRpcHarness.Value(await host.Call("quantum.plugin.codex.codex.ensure-auto-connected", new CodexAutoConnectRequest()));
            Assert.Equal(2, fake.AutomaticCalls);
            var connected = await client.ConnectAsync(new()
            {
                WorkingDirectory = "workspace",
                Model = "model-a"
            }, context);
            Assert.True(connected.IsSuccess, connected.Message);
            Assert.Equal("thread-1", connected.Value!.ThreadId);
            Assert.False((await client.SendAsync(new()
            {
                Prompt = " "
            }, context)).IsSuccess);
            Assert.True((await client.ChangeModelAsync(new()
            {
                Model = "model-b"
            }, context)).IsSuccess);
            var snapshot = OfficialRpcHarness.Value(await host.Call("codex.snapshot", new
            {
            }));
            Assert.Equal("model-b", snapshot.GetProperty("selectedModel").GetString());
            Assert.Equal("thread-1", snapshot.GetProperty("threadId").GetString());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(new()
            {
                Prompt = "Cancelled"
            }, context, cancelled.Token));
            Assert.Null(fake.LastPrompt);
            Assert.True((await client.DisconnectAsync(new(), context)).IsSuccess);
        });
    }

    [Fact]
    public async Task ProductionHandlersAreDiscoverableAndReadOnlyCallsNeedNoAppServer()
    {
        await using var host = Host();
        await host.AssertCatalog("quantum.plugin.codex", "codex", ["snapshot", "models", "connect", "new-thread", "change-model", "send", "disconnect", "ensure-auto-connected"]);
        var snapshot = OfficialRpcHarness.Value(await host.Call("quantum.plugin.codex.codex.snapshot", new
        {
        }));
        Assert.Equal("Disconnected", snapshot.GetProperty("connectionState").GetString());
        Assert.NotEmpty(OfficialRpcHarness.Value(await host.Call("codex.models", new
        {
        })).EnumerateArray());
        OfficialRpcHarness.Value(await host.Call("codex.disconnect", new
        {
        }));
        var invalidState = await host.Call("codex.send", new CodexSendRequest
        {
            Prompt = "test"
        });
        Assert.Equal("codex_invalid_state", invalidState.GetProperty("errorCode").GetString());
        var invalidModelState = await host.Call("codex.change-model", new CodexChangeModelRequest
        {
            Model = "gpt-5.6-sol"
        });
        Assert.Equal("codex_invalid_state", invalidModelState.GetProperty("errorCode").GetString());
        var invalidDirectory = await host.Call("codex.connect", new CodexConnectRequest
        {
            WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });
        Assert.Equal("codex_connection_failed", invalidDirectory.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task HostDispatchRoutesAllCommandsToTheSharedApplicationService()
    {
        var fake = new FakeCodex();
        await using var host = Host(fake);
        OfficialRpcHarness.Value(await host.Call("codex.connect", new CodexConnectRequest
        {
            WorkingDirectory = "workspace",
            Model = "model-a"
        }));
        Assert.Equal("workspace", fake.Snapshot.WorkingDirectory);
        var thread = OfficialRpcHarness.Value(await host.Call("codex.new-thread", new CodexNewThreadRequest
        {
            Model = "model-b"
        }));
        Assert.Equal("thread-2", thread.GetProperty("threadId").GetString());
        var changed = OfficialRpcHarness.Value(await host.Call("quantum.plugin.codex.codex.change-model", new CodexChangeModelRequest
        {
            Model = "model-a"
        }));
        Assert.Equal("thread-2", changed.GetProperty("threadId").GetString());
        Assert.Equal("model-a", changed.GetProperty("selectedModel").GetString());
        var send = OfficialRpcHarness.Value(await host.Call("codex.send", new CodexSendRequest
        {
            Prompt = "Test prompt"
        }));
        Assert.True(send.GetProperty("isBusy").GetBoolean());
        Assert.Equal("Test prompt", fake.LastPrompt);
        OfficialRpcHarness.Value(await host.Call("codex.snapshot", new
        {
        }));
        OfficialRpcHarness.Value(await host.Call("codex.models", new
        {
        }));
        OfficialRpcHarness.Value(await host.Call("codex.disconnect", new
        {
        }));
        Assert.Equal(CodexConnectionState.Disconnected, fake.Snapshot.ConnectionState);
        Assert.Equal(new[] { "connect", "new-thread", "change-model", "send", "disconnect" }, fake.Commands);
    }

    [Fact]
    public async Task InvalidRequestsFailedConnectionsAndCancellationAreReported()
    {
        var fake = new FakeCodex { FailConnect = true };
        await using var host = Host(fake);
        var invalidModel = await host.Call("codex.change-model", new CodexChangeModelRequest
        {
            Model = " "
        });
        Assert.Equal("codex_invalid_request", invalidModel.GetProperty("errorCode").GetString());
        var invalidPrompt = await host.Call("codex.send", new CodexSendRequest
        {
            Prompt = " "
        });
        Assert.Equal("codex_invalid_request", invalidPrompt.GetProperty("errorCode").GetString());
        var failed = await host.Call("codex.connect", new CodexConnectRequest
        {
            WorkingDirectory = "workspace",
            Model = "model-a"
        });
        Assert.Equal("codex_connection_failed", failed.GetProperty("errorCode").GetString());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("codex.send", new CodexSendRequest
        {
            Prompt = "test"
        }, cancellationToken: cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("codex.change-model", new CodexChangeModelRequest
        {
            Model = "model-b"
        }, cancellationToken: cancelled.Token));
        Assert.DoesNotContain("change-model", fake.Commands);
        Assert.Null(fake.LastPrompt);
    }

    private sealed class UnavailableInvoker : IRpcInvoker
    {
        public Task<Result<T>> InvokeAsync<T>(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<T>>(Result.Fail("not_available", "Test has no external RPC services."));
        public Task<Result> InvokeAsync(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => Task.FromResult<Result>(Result.Fail("not_available", "Test has no external RPC services."));
    }

    private sealed class FakeCodex : ICodexRuntime
    {
        public event Action? Changed { add { } remove { } }
        public int AutomaticCalls
        {
            get; private set;
        }

        public Task EnsureAutoConnectedAsync()
        {
            AutomaticCalls++;
            return Task.CompletedTask;
        }

        public bool FailConnect
        {
            get; init;
        }

        public string? LastPrompt
        {
            get; private set;
        }

        public List<string> Commands { get; } = [];
        public CodexIntegrationSnapshot Snapshot
        {
            get; private set;
        } = new()
        {
            ConnectionState = CodexConnectionState.Disconnected,
            ThreadId = null,
            IsBusy = false,
            ToolCount = 0,
            WorkingDirectory = "",
            Models = [new()
    {
        Id = "model-a",
        DisplayName = "A"
    }, new()
    {
        Id = "model-b",
        DisplayName = "B"
    }

    ],
            SelectedModel = "model-a",
            Messages = [],
            ErrorMessage = null
        };

        public Task ConnectAsync(string workingDirectory, string model, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add("connect");
            Snapshot = Snapshot with
            {
                ConnectionState = FailConnect ? CodexConnectionState.Failed : CodexConnectionState.Connected,
                WorkingDirectory = workingDirectory,
                SelectedModel = model,
                ThreadId = "thread-1",
                ErrorMessage = FailConnect ? "Failed" : null
            };
            return Task.CompletedTask;
        }

        public Task StartNewThreadAsync(string? model = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add("new-thread");
            Snapshot = Snapshot with
            {
                ThreadId = "thread-2",
                SelectedModel = model ?? Snapshot.SelectedModel
            };
            return Task.CompletedTask;
        }

        public Task ChangeModelAsync(string model, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add("change-model");
            Snapshot = Snapshot with
            {
                SelectedModel = model
            };
            return Task.CompletedTask;
        }

        public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add("send");
            LastPrompt = prompt;
            Snapshot = Snapshot with
            {
                IsBusy = true
            };
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add("disconnect");
            Snapshot = Snapshot with
            {
                ConnectionState = CodexConnectionState.Disconnected,
                ThreadId = null,
                IsBusy = false
            };
            return Task.CompletedTask;
        }
    }
}
