using Microsoft.Extensions.Logging.Abstractions;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Quantum.Tests;

public sealed class CodexConversationTests
{
    private static CodexRuntime Service(FakeClient client) => new(new CatalogInvoker(),
        NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => client);

    [Fact]
    public async Task ModelChangesPreserveThreadHistoryAndToolsAndApplyToEverySubsequentTurn()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexRuntime.DefaultModel);
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
        Assert.Equal(new[] { CodexRuntime.DefaultModel, "gpt-5.6-sol", "gpt-5.6-sol" }, client.Turns.Select(turn => turn.Model));
        Assert.Contains(service.Snapshot.Messages, message => message.Text == "Remember this conversation");
    }

    [Fact]
    public async Task OnlyExplicitNewConversationReplacesTheThreadAndClearsHistory()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexRuntime.DefaultModel);
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
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync("gpt-5.6-sol"));
        await service.ConnectAsync(Environment.CurrentDirectory, CodexRuntime.DefaultModel);
        var before = service.Snapshot;
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync("unknown-model"));
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync(" "));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ChangeModelAsync("gpt-5.6-sol", cancelled.Token));
        Assert.Equal(before.SelectedModel, service.Snapshot.SelectedModel);
        Assert.Equal(before.Messages, service.Snapshot.Messages);
        await service.SendAsync("Active turn");
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync("gpt-5.6-sol"));
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

    [Fact]
    public async Task VoiceUsesTheSpeechCapabilityAndKeepsTheExistingConversation()
    {
        var main = new FakeClient();
        var transcription = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new CatalogInvoker(transcription.Task);
        await using var service = new CodexRuntime(invoker, NullLoggerFactory.Instance,
            NullLogger<CodexRuntime>.Instance, clientFactory: () => main);
        await service.ConnectAsync(Path.GetTempPath(), CodexRuntime.DefaultModel);
        var threadId = service.Snapshot.ThreadId;
        var sending = service.SendAsync(new CodexSendRequest
        {
            Prompt = "",
            Attachments = [new MobileAttachment { Name = "voice.wav", ContentType = "audio/wav", DataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }) }]
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!service.Snapshot.IsBusy)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.True(service.Snapshot.Messages.Last().IsTranscribing);
        Assert.Empty(main.Turns);
        transcription.SetResult("Recognized speech");
        await sending.WaitAsync(timeout.Token);
        Assert.Equal(threadId, main.Turns.Single().ThreadId);
        Assert.Equal("Recognized speech", service.Snapshot.Messages.Last().Text);
        Assert.Equal("voice.wav", service.Snapshot.Messages.Last().Attachments.Single().Name);
        Assert.False(service.Snapshot.Messages.Last().IsTranscribing);
        Assert.Equal(1, main.ThreadStarts);
        Assert.Equal(1, invoker.SpeechCalls);
    }

    [Fact]
    public void MediaTurnUsesImageAndFileInputsWithoutChangingTheConversation()
    {
        var files = new[]
        {
            new CodexAttachmentFile { Name = "image.png", ContentType = "image/png", Path = "/selected/image.png", Size = 1 },
            new CodexAttachmentFile { Name = "document.pdf", ContentType = "application/pdf", Path = "/selected/document.pdf", Size = 1 }
        };
        var payload = JsonSerializer.SerializeToElement(CodexAppServerClient.CreateMediaTurnParameters("existing", "Compare", "model", files));
        Assert.Equal("existing", payload.GetProperty("threadId").GetString());
        Assert.Equal("localImage", payload.GetProperty("input")[1].GetProperty("type").GetString());
        Assert.Equal("mention", payload.GetProperty("input")[2].GetProperty("type").GetString());
        Assert.Equal("/selected/document.pdf", payload.GetProperty("input")[2].GetProperty("path").GetString());
    }

    [Theory]
    [InlineData("../outside.txt", "AQ==")]
    [InlineData("file.txt", "not base64")]
    public async Task AttachmentValidationFailsBeforeAnyModelTurn(string name, string encoded)
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Path.GetTempPath(), CodexRuntime.DefaultModel);
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.SendAsync(new CodexSendRequest
        {
            Prompt = "",
            Attachments = [new MobileAttachment { Name = name, ContentType = "text/plain", DataBase64 = encoded }]
        }));
        Assert.Empty(client.Turns);
        Assert.False(service.Snapshot.IsBusy);
    }

    private sealed class CatalogInvoker(Task<string>? transcript = null) : IRpcInvoker
    {
        public int SpeechCalls
        {
            get; private set;
        }
        public async Task<Result<T>> InvokeAsync<T>(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
        {
            if (rpcName == "speech.transcribe")
            {
                SpeechCalls++;
                return (T)(object)await (transcript ?? Task.FromResult("Recognized speech")).WaitAsync(cancellationToken);
            }
            return (T)(object)JsonSerializer.SerializeToElement(new
            {
                services = new[] { new { methods = new[] { new { qualifiedName = "quantum.plugin.calendar.calendar.list", description = "Read calendar", returnsValue = true, inputSchema = new { type = "object" } } } } }
            });
        }
        public Task<Result> InvokeAsync(string rpcName, object payload, Context context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeClient : ICodexAppServerClient
    {
        public event Func<string, JsonElement, Task>? NotificationReceived;
        public event Action<Exception?>? ConnectionClosed { add { } remove { } }
        public event Func<CodexDynamicToolCall, CancellationToken, Task<CodexDynamicToolResult>>? DynamicToolCallReceived { add { } remove { } }
        public TaskCompletionSource<string>? Transcript
        {
            get; init;
        }
        public Task<JsonElement> StartMediaTurnAsync(string threadId, string prompt, string model, CodexAttachmentFile[] files, CancellationToken token)
            => StartTurnAsync(threadId, prompt, model, token);
        public int ThreadStarts
        {
            get; private set;
        }

        public List<(string ThreadId, string Model)> Turns { get; } = [];
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult(CodexRuntime.InitialModels);
        public Task<string> StartThreadAsync(string workingDirectory, string model, JsonArray dynamicTools, CancellationToken cancellationToken)
            => Task.FromResult($"thread-{++ThreadStarts}");
        public Task<JsonElement> StartTurnAsync(string threadId, string prompt, string model, CancellationToken cancellationToken)
        {
            Turns.Add((threadId, model));
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
            }));
        }

        public Task CompleteTurn() => NotificationReceived!("turn/completed", JsonSerializer.SerializeToElement(new { turn = new { status = "completed" } }));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
