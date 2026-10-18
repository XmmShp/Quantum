using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.Infrastructure;
using Quantum.Plugin.Abstraction;

namespace Quantum.Tests;

public sealed class CodexConversationTests
{
    private static CodexIntegrationService Service(FakeClient client) => new(new CatalogInvoker(),
        NullLoggerFactory.Instance, NullLogger<CodexIntegrationService>.Instance, clientFactory: () => client);

    [Fact]
    public async Task ModelChangesPreserveThreadHistoryAndToolsAndApplyToEverySubsequentTurn()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexIntegrationService.DefaultModel);
        await service.SendAsync("Remember this conversation");
        await client.CompleteTurn();
        var before = service.Snapshot;
        Assert.Equal(1, before.ToolCount);
        await service.ChangeModelAsync("gpt-5.6-sol");
        await service.ChangeModelAsync("gpt-5.6-sol");
        Assert.Equal(before.ThreadId, service.Snapshot.ThreadId);
        Assert.Equal(before.Messages, service.Snapshot.Messages);
        Assert.Equal(before.ToolCount, service.Snapshot.ToolCount);
        Assert.Equal(1, client.ThreadStarts);
        Assert.Single(client.Turns);
        await service.SendAsync("Continue");
        await client.CompleteTurn();
        await service.SendAsync("Continue again");
        Assert.All(client.Turns, turn => Assert.Equal(before.ThreadId, turn.ThreadId));
        Assert.Equal(new[] { CodexIntegrationService.DefaultModel, "gpt-5.6-sol", "gpt-5.6-sol" }, client.Turns.Select(turn => turn.Model));
        Assert.Contains(service.Snapshot.Messages, message => message.Text == "Remember this conversation");
    }

    [Fact]
    public async Task OnlyExplicitNewConversationReplacesTheThreadAndClearsHistory()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexIntegrationService.DefaultModel);
        await service.SendAsync("Old message");
        await client.CompleteTurn();
        await service.ChangeModelAsync("gpt-5.6-sol");
        var oldId = service.Snapshot.ThreadId;
        await service.StartNewThreadAsync();
        Assert.NotEqual(oldId, service.Snapshot.ThreadId);
        Assert.Equal("gpt-5.6-sol", service.Snapshot.SelectedModel);
        Assert.DoesNotContain(service.Snapshot.Messages, message => message.Role == CodexMessageRole.User);
        Assert.Equal(2, client.ThreadStarts);
    }

    [Fact]
    public async Task BusyInvalidCancelledAndDisconnectedSelectionsLeaveConversationUntouched()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeModelAsync("gpt-5.6-sol"));
        await service.ConnectAsync(Environment.CurrentDirectory, CodexIntegrationService.DefaultModel);
        var before = service.Snapshot;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeModelAsync("unknown-model"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangeModelAsync(" "));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ChangeModelAsync("gpt-5.6-sol", cancelled.Token));
        Assert.Equal(before.SelectedModel, service.Snapshot.SelectedModel);
        Assert.Equal(before.Messages, service.Snapshot.Messages);
        await service.SendAsync("Active turn");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeModelAsync("gpt-5.6-sol"));
        Assert.Equal(before.SelectedModel, service.Snapshot.SelectedModel);
        Assert.Equal(before.ThreadId, service.Snapshot.ThreadId);
        Assert.Equal(1, client.ThreadStarts);
    }

    [Fact]
    public void TurnWireParametersCarryTheExistingThreadAndSelectedModel()
    {
        var payload = JsonSerializer.SerializeToElement(CodexAppServerClient.CreateTurnParameters("thread-existing", "Continue", "gpt-5.6-sol"));
        Assert.Equal("thread-existing", payload.GetProperty("threadId").GetString());
        Assert.Equal("gpt-5.6-sol", payload.GetProperty("model").GetString());
        Assert.Equal("Continue", payload.GetProperty("input")[0].GetProperty("text").GetString());
    }

    private sealed class CatalogInvoker : IRpcInvoker
    {
        public Task<Result<T>> InvokeAsync<T>(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<T>>((T)(object)JsonSerializer.SerializeToElement(new { services = new[] { new { methods = new[] { new { qualifiedName = "quantum.plugin.calendar.calendar.list", description = "Read calendar", returnsValue = true, inputSchema = new { type = "object" } } } } } }));
        public Task<Result> InvokeAsync(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeClient : ICodexAppServerClient
    {
        public event Func<string, JsonElement, Task>? NotificationReceived;
        public event Action<Exception?>? ConnectionClosed { add { } remove { } }
        public event Func<CodexDynamicToolCall, CancellationToken, Task<CodexDynamicToolResult>>? DynamicToolCallReceived { add { } remove { } }
        public int ThreadStarts { get; private set; }
        public List<(string ThreadId, string Model)> Turns { get; } = [];
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult(CodexIntegrationService.InitialModels);
        public Task<string> StartThreadAsync(string workingDirectory, string model, JsonArray dynamicTools, CancellationToken cancellationToken)
            => Task.FromResult($"thread-{++ThreadStarts}");
        public Task<JsonElement> StartTurnAsync(string threadId, string prompt, string model, CancellationToken cancellationToken)
        {
            Turns.Add((threadId, model));
            return Task.FromResult(JsonSerializer.SerializeToElement(new { }));
        }
        public Task CompleteTurn() => NotificationReceived!("turn/completed", JsonSerializer.SerializeToElement(new { turn = new { status = "completed" } }));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
