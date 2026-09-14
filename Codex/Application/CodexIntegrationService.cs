using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Infrastructure;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex.Application;

internal sealed class CodexIntegrationService(
    IRpcInvoker rpcInvoker,
    ILoggerFactory loggerFactory,
    ILogger<CodexIntegrationService> logger) : ICodexIntegrationService, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<CodexChatMessage> _messages = [];
    private readonly Dictionary<string, PendingQuantumRpcCall> _pendingCalls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _approvals = new(StringComparer.Ordinal);
    private IReadOnlyList<QuantumRpcTool> _tools = [];
    private CodexAppServerClient? _client;
    private CodexConnectionState _connectionState;
    private string? _threadId;
    private bool _isBusy;
    private string _workingDirectory = Environment.CurrentDirectory;
    private string? _errorMessage;
    private bool _disposed;

    public event Action? Changed;

    public CodexIntegrationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new CodexIntegrationSnapshot(
                    _connectionState,
                    _threadId,
                    _isBusy,
                    _tools.Count,
                    _workingDirectory,
                    _messages.ToArray(),
                    _pendingCalls.Values.ToArray(),
                    _errorMessage);
            }
        }
    }

    public async Task ConnectAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CodexAppServerClient? staleClient = null;
            lock (_sync)
            {
                if (_client is not null && _connectionState is CodexConnectionState.Connected
                    or CodexConnectionState.Connecting)
                {
                    return;
                }

                if (_client is not null)
                {
                    staleClient = _client;
                    _client = null;
                }
            }

            if (staleClient is not null)
            {
                staleClient.NotificationReceived -= HandleNotificationAsync;
                staleClient.DynamicToolCallReceived -= HandleDynamicToolCallAsync;
                staleClient.ConnectionClosed -= HandleConnectionClosed;
                await staleClient.DisposeAsync().ConfigureAwait(false);
            }

            SetConnectionState(CodexConnectionState.Connecting, null);
            var normalizedDirectory = Path.GetFullPath(
                string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : workingDirectory.Trim());
            if (!Directory.Exists(normalizedDirectory))
            {
                throw new DirectoryNotFoundException($"Working directory '{normalizedDirectory}' does not exist.");
            }

            var catalogResult = await rpcInvoker.InvokeAsync<JsonElement>(
                "quantum.rpc.catalog",
                new { },
                Context.Empty,
                cancellationToken).ConfigureAwait(false);
            if (!catalogResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Could not read Quantum RPC catalog: {catalogResult.ErrorCode}: {catalogResult.Message}");
            }

            var tools = QuantumRpcToolCatalog.Parse(catalogResult.Value);
            var client = new CodexAppServerClient(loggerFactory.CreateLogger<CodexAppServerClient>());
            client.NotificationReceived += HandleNotificationAsync;
            client.DynamicToolCallReceived += HandleDynamicToolCallAsync;
            client.ConnectionClosed += HandleConnectionClosed;
            try
            {
                await client.StartAsync(cancellationToken).ConfigureAwait(false);
                var threadId = await client.StartThreadAsync(
                    normalizedDirectory,
                    QuantumRpcToolCatalog.ToDynamicTools(tools),
                    cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    _client = client;
                    _tools = tools;
                    _workingDirectory = normalizedDirectory;
                    _threadId = threadId;
                    _connectionState = CodexConnectionState.Connected;
                    _errorMessage = null;
                    _messages.Add(new CodexChatMessage(
                        Guid.NewGuid(),
                        CodexMessageRole.System,
                        $"Codex connected. {tools.Count} Quantum RPC tools are available.",
                        DateTimeOffset.Now));
                }

                NotifyChanged();
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not connect the Quantum Codex integration.");
            SetConnectionState(CodexConnectionState.Failed, exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StartNewThreadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CodexAppServerClient client;
            IReadOnlyList<QuantumRpcTool> tools;
            string workingDirectory;
            lock (_sync)
            {
                client = _client ?? throw new InvalidOperationException("Codex is not connected.");
                tools = _tools;
                workingDirectory = _workingDirectory;
            }

            var threadId = await client.StartThreadAsync(
                workingDirectory,
                QuantumRpcToolCatalog.ToDynamicTools(tools),
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _threadId = threadId;
                _isBusy = false;
                _errorMessage = null;
                _messages.Clear();
                _messages.Add(new CodexChatMessage(
                    Guid.NewGuid(),
                    CodexMessageRole.System,
                    $"New Codex conversation started with {tools.Count} Quantum RPC tools.",
                    DateTimeOffset.Now));
            }

            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        CodexAppServerClient client;
        string threadId;
        lock (_sync)
        {
            client = _client ?? throw new InvalidOperationException("Codex is not connected.");
            threadId = _threadId ?? throw new InvalidOperationException("No Codex conversation is active.");
            if (_isBusy)
            {
                throw new InvalidOperationException("Wait for the current Codex turn to finish.");
            }

            _isBusy = true;
            _errorMessage = null;
            _messages.Add(new CodexChatMessage(
                Guid.NewGuid(),
                CodexMessageRole.User,
                prompt.Trim(),
                DateTimeOffset.Now));
        }

        NotifyChanged();
        try
        {
            await client.StartTurnAsync(threadId, prompt.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _isBusy = false;
                _errorMessage = exception.Message;
            }

            NotifyChanged();
            throw;
        }
    }

    public Task ResolveToolCallAsync(
        string callId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_approvals.TryGetValue(callId, out var completion))
        {
            completion.TrySetResult(approved);
        }

        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CodexAppServerClient? client;
            lock (_sync)
            {
                client = _client;
                _client = null;
                _threadId = null;
                _isBusy = false;
                _connectionState = CodexConnectionState.Disconnected;
                _errorMessage = null;
                _pendingCalls.Clear();
            }

            foreach (var approval in _approvals.Values)
            {
                approval.TrySetResult(false);
            }

            if (client is not null)
            {
                client.NotificationReceived -= HandleNotificationAsync;
                client.DynamicToolCallReceived -= HandleDynamicToolCallAsync;
                client.ConnectionClosed -= HandleConnectionClosed;
                await client.DisposeAsync().ConfigureAwait(false);
            }

            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _operationGate.Dispose();
    }

    private Task HandleNotificationAsync(string method, JsonElement parameters)
    {
        lock (_sync)
        {
            switch (method)
            {
                case "item/agentMessage/delta":
                    AppendAssistantDelta(parameters.GetProperty("delta").GetString() ?? string.Empty);
                    break;
                case "turn/completed":
                    _isBusy = false;
                    CompleteStreamingMessage();
                    if (parameters.TryGetProperty("turn", out var turn)
                        && turn.TryGetProperty("status", out var status)
                        && status.GetString() == "failed")
                    {
                        _errorMessage = ReadTurnError(turn) ?? "The Codex turn failed.";
                    }

                    break;
                case "error":
                    _errorMessage = parameters.TryGetProperty("message", out var message)
                        ? message.GetString()
                        : parameters.GetRawText();
                    _isBusy = false;
                    CompleteStreamingMessage();
                    break;
            }
        }

        NotifyChanged();
        return Task.CompletedTask;
    }

    private void HandleConnectionClosed(Exception? exception)
    {
        lock (_sync)
        {
            if (_connectionState == CodexConnectionState.Disconnected)
            {
                return;
            }

            _connectionState = CodexConnectionState.Failed;
            _isBusy = false;
            _errorMessage = exception?.Message ?? "The Codex app-server connection closed.";
            CompleteStreamingMessage();
        }

        foreach (var approval in _approvals.Values)
        {
            approval.TrySetResult(false);
        }

        NotifyChanged();
    }

    private async Task<CodexDynamicToolResult> HandleDynamicToolCallAsync(
        CodexDynamicToolCall call,
        CancellationToken cancellationToken)
    {
        if (call.Namespace is not null
            && !string.Equals(call.Namespace, QuantumRpcToolCatalog.Namespace, StringComparison.Ordinal))
        {
            return new CodexDynamicToolResult(false, $"Unknown tool namespace '{call.Namespace}'.");
        }

        QuantumRpcTool? tool;
        lock (_sync)
        {
            tool = _tools.FirstOrDefault(candidate =>
                string.Equals(candidate.ToolName, call.Tool, StringComparison.Ordinal));
        }

        if (tool is null)
        {
            return new CodexDynamicToolResult(false, $"Unknown Quantum tool '{call.Tool}'.");
        }

        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_approvals.TryAdd(call.CallId, approval))
        {
            return new CodexDynamicToolResult(false, "A duplicate Quantum RPC call id was received.");
        }

        lock (_sync)
        {
            _pendingCalls[call.CallId] = new PendingQuantumRpcCall(
                call.CallId,
                tool.ToolName,
                tool.RpcName,
                tool.Description,
                call.Arguments.Clone());
        }

        NotifyChanged();
        try
        {
            if (!await approval.Task.WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                AddToolMessage($"Declined Quantum RPC {tool.RpcName}.");
                return new CodexDynamicToolResult(false, "The user declined this Quantum RPC call.");
            }

            var result = await InvokeQuantumRpcAsync(tool, call.Arguments, cancellationToken)
                .ConfigureAwait(false);
            AddToolMessage($"Quantum RPC {tool.RpcName}: {(result.Success ? "completed" : "failed")}.");
            return result;
        }
        finally
        {
            _approvals.TryRemove(call.CallId, out _);
            lock (_sync)
            {
                _pendingCalls.Remove(call.CallId);
            }

            NotifyChanged();
        }
    }

    private async Task<CodexDynamicToolResult> InvokeQuantumRpcAsync(
        QuantumRpcTool tool,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (tool.ReturnsValue)
        {
            var result = await rpcInvoker.InvokeAsync<JsonElement>(
                tool.RpcName,
                arguments,
                Context.Empty,
                cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? new CodexDynamicToolResult(true, result.Value.GetRawText())
                : Failure(result.ErrorCode, result.Message);
        }

        var emptyResult = await rpcInvoker.InvokeAsync(
            tool.RpcName,
            arguments,
            Context.Empty,
            cancellationToken).ConfigureAwait(false);
        return emptyResult.IsSuccess
            ? new CodexDynamicToolResult(true, "{\"isSuccess\":true}")
            : Failure(emptyResult.ErrorCode, emptyResult.Message);
    }

    private static CodexDynamicToolResult Failure(string errorCode, string message)
        => new(false, JsonSerializer.Serialize(new { isSuccess = false, errorCode, message }));

    private void AppendAssistantDelta(string delta)
    {
        var index = _messages.FindLastIndex(static message =>
            message.Role == CodexMessageRole.Assistant && message.IsStreaming);
        if (index < 0)
        {
            _messages.Add(new CodexChatMessage(
                Guid.NewGuid(),
                CodexMessageRole.Assistant,
                delta,
                DateTimeOffset.Now,
                IsStreaming: true));
            return;
        }

        _messages[index] = _messages[index] with { Text = _messages[index].Text + delta };
    }

    private void CompleteStreamingMessage()
    {
        var index = _messages.FindLastIndex(static message => message.IsStreaming);
        if (index >= 0)
        {
            _messages[index] = _messages[index] with { IsStreaming = false };
        }
    }

    private void AddToolMessage(string text)
    {
        lock (_sync)
        {
            _messages.Add(new CodexChatMessage(
                Guid.NewGuid(),
                CodexMessageRole.Tool,
                text,
                DateTimeOffset.Now));
        }

        NotifyChanged();
    }

    private static string? ReadTurnError(JsonElement turn)
        => turn.TryGetProperty("error", out var error)
            && error.ValueKind != JsonValueKind.Null
            && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;

    private void SetConnectionState(CodexConnectionState state, string? errorMessage)
    {
        lock (_sync)
        {
            _connectionState = state;
            _errorMessage = errorMessage;
        }

        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
