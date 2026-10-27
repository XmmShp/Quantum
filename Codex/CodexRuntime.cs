using Microsoft.Extensions.Logging;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex;

internal sealed class CodexRuntime : ICodexRuntime, IAsyncDisposable
{
    private readonly IRpcInvoker _rpcInvoker;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CodexRuntime> _logger;
    private readonly TimeSpan? _automaticConnectionTimeout;
    private readonly Func<ICodexAppServerClient>? _clientFactory;
    internal const string DefaultModel = "gpt-5.6-luna";

    internal static readonly IReadOnlyList<CodexModelOption> InitialModels =
    [
        new()
{
    Id = "gpt-5.6-luna",
    DisplayName = "GPT-5.6 Luna"
},
        new()
{
    Id = "gpt-5.6-terra",
    DisplayName = "GPT-5.6 Terra"
},
        new()
{
    Id = "gpt-5.6-sol",
    DisplayName = "GPT-5.6 Sol"
},
        new()
{
    Id = "gpt-6-astra",
    DisplayName = "GPT-6 Astra"
}
    ];

    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<CodexChatMessage> _messages = [];
    private readonly ConcurrentDictionary<string, byte> _activeToolCalls = new(StringComparer.Ordinal);
    private IReadOnlyList<QuantumRpcTool> _tools = [];
    private IReadOnlyList<CodexModelOption> _models = InitialModels;
    private string _selectedModel = DefaultModel;
    private ICodexAppServerClient? _client;
    private CodexConnectionState _connectionState;
    private string? _threadId;
    private bool _isBusy;
    private CancellationTokenSource? _sendingCancellation;
    private string _workingDirectory = Environment.CurrentDirectory;
    private string? _errorMessage;
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _automaticConnection;
    private Task? _automaticConnectionTask;
    private bool _automaticConnectionSuppressed;

    public CodexRuntime(
        IRpcInvoker rpcInvoker,
        ILoggerFactory loggerFactory,
        ILogger<CodexRuntime> logger,
        TimeSpan? automaticConnectionTimeout = null,
        Func<ICodexAppServerClient>? clientFactory = null)
    {
        _rpcInvoker = rpcInvoker;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _automaticConnectionTimeout = automaticConnectionTimeout;
        _clientFactory = clientFactory;
    }

    public Task EnsureAutoConnectedAsync()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_automaticConnectionTask is not null)
            {
                return _automaticConnectionTask;
            }

            if (_automaticConnectionSuppressed || _connectionState != CodexConnectionState.Disconnected)
            {
                return Task.CompletedTask;
            }

            _automaticConnectionTask = AutoConnectAsync();
            return _automaticConnectionTask;
        }
    }

    private async Task AutoConnectAsync()
    {
        // First interactive render runs after the host publishes the RPC registry.
        // Yield before raising Changed so no UI callbacks execute under _sync.
        await Task.Yield();
        CancellationToken token;
        lock (_sync)
        {
            if (_disposed || _automaticConnectionSuppressed || _connectionState != CodexConnectionState.Disconnected)
            {
                return;
            }

            _automaticConnection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _automaticConnection.CancelAfter(_automaticConnectionTimeout ?? TimeSpan.FromSeconds(30));
            token = _automaticConnection.Token;
        }
        try
        {
            var snapshot = Snapshot;
            await ConnectAsync(snapshot.WorkingDirectory, snapshot.SelectedModel, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetAutomaticFailure(
                "Automatic connection timed out. Check the local Codex installation and sign-in, then retry.");
        }
        catch (Exception exception)
        {
            SetAutomaticFailure(exception.Message);
        }
    }

    private void SetAutomaticFailure(string message)
    {
        lock (_sync)
        {
            if (_disposed || _automaticConnectionSuppressed
                || _connectionState is CodexConnectionState.Connected or CodexConnectionState.Connecting)
            {
                return;
            }

            _connectionState = CodexConnectionState.Failed;
            _errorMessage = message;
        }
        NotifyChanged();
    }

    public event Action? Changed;

    public CodexIntegrationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new CodexIntegrationSnapshot
                {
                    ConnectionState = _connectionState,
                    ThreadId = _threadId,
                    IsBusy = _isBusy,
                    ToolCount = _tools.Count,
                    WorkingDirectory = _workingDirectory,
                    Models = _models,
                    SelectedModel = _selectedModel,
                    Messages = _messages.ToArray(),
                    ErrorMessage = _errorMessage
                };
            }
        }
    }

    public async Task ConnectAsync(
        string workingDirectory,
        string model,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ICodexAppServerClient? staleClient = null;
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
                throw new DomainException("codex_invalid_request", $"Working directory '{normalizedDirectory}' does not exist.");
            }

            var catalogResult = await _rpcInvoker.InvokeAsync<JsonElement>(
                "quantum.rpc.catalog",
                new
                {
                },
                Context.Empty,
                cancellationToken).ConfigureAwait(false);
            if (!catalogResult.IsSuccess)
            {
                throw new DomainException("codex_invalid_state",
                    $"Could not read Quantum RPC catalog: {catalogResult.ErrorCode}: {catalogResult.Message}");
            }

            var tools = QuantumRpcToolCatalog.Parse(catalogResult.Value);
            var selectedModel = ResolveModel(model, InitialModels);
            var client = _clientFactory?.Invoke()
                ?? new CodexAppServerClient(_loggerFactory.CreateLogger<CodexAppServerClient>());
            client.NotificationReceived += HandleNotificationAsync;
            client.DynamicToolCallReceived += HandleDynamicToolCallAsync;
            client.ConnectionClosed += HandleConnectionClosed;
            try
            {
                await client.StartAsync(cancellationToken).ConfigureAwait(false);
                var availableModels = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                if (availableModels.Count > 0)
                {
                    selectedModel = ResolveModel(selectedModel, availableModels);
                }

                var threadId = await client.StartThreadAsync(
                    normalizedDirectory,
                    selectedModel,
                    QuantumRpcToolCatalog.ToDynamicTools(tools),
                    cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    _client = client;
                    _tools = tools;
                    _models = availableModels.Count > 0 ? availableModels : InitialModels;
                    _selectedModel = selectedModel;
                    _workingDirectory = normalizedDirectory;
                    _threadId = threadId;
                    _connectionState = CodexConnectionState.Connected;
                    _errorMessage = null;
                    _messages.Add(new CodexChatMessage
                    {
                        Id = Guid.NewGuid(),
                        Role = CodexMessageRole.System,
                        Text = $"Codex connected with {selectedModel}. {tools.Count} Quantum RPC tools are available.",
                        CreatedAt = DateTimeOffset.Now
                    });
                }

                NotifyChanged();
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            SetConnectionState(CodexConnectionState.Disconnected, null);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not connect the Quantum Codex integration.");
            SetConnectionState(CodexConnectionState.Failed, exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StartNewThreadAsync(
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ICodexAppServerClient client;
            IReadOnlyList<QuantumRpcTool> tools;
            string workingDirectory;
            string selectedModel;
            lock (_sync)
            {
                client = _client ?? throw new DomainException("codex_invalid_state", "Codex is not connected.");
                tools = _tools;
                workingDirectory = _workingDirectory;
                selectedModel = ResolveModel(model ?? _selectedModel, _models);
            }

            var threadId = await client.StartThreadAsync(
                workingDirectory,
                selectedModel,
                QuantumRpcToolCatalog.ToDynamicTools(tools),
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _threadId = threadId;
                _selectedModel = selectedModel;
                _isBusy = false;
                _errorMessage = null;
                _messages.Clear();
                _messages.Add(new CodexChatMessage
                {
                    Id = Guid.NewGuid(),
                    Role = CodexMessageRole.System,
                    Text = $"New {selectedModel} conversation started with {tools.Count} Quantum RPC tools.",
                    CreatedAt = DateTimeOffset.Now
                });
            }

            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ChangeModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_connectionState != CodexConnectionState.Connected || _threadId is null)
                {
                    throw new DomainException("codex_invalid_state", "Codex is not connected.");
                }

                if (_isBusy)
                {
                    throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
                }

                _selectedModel = ResolveModel(model, _models);
            }
            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
        => SendAsync(new CodexSendRequest { Prompt = prompt }, cancellationToken);

    public async Task SendAsync(CodexSendRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Attachments is null || string.IsNullOrWhiteSpace(request.Prompt) && request.Attachments.Length == 0)
        {
            throw new DomainException("codex_invalid_request", "A prompt or attachment is required.");
        }
        lock (_sync)
        {
            if (_client is null || _threadId is null)
            {
                throw new DomainException("codex_invalid_state", "Codex is not connected.");
            }

            if (_isBusy)
            {
                throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
            }
        }
        var files = await CodexAttachmentStore.StageAsync(request.Attachments, cancellationToken);
        ICodexAppServerClient client;
        string threadId;
        string model;
        CodexChatMessage message;
        CancellationToken sendingToken;
        lock (_sync)
        {
            client = _client ?? throw new DomainException("codex_invalid_state", "Codex is not connected.");
            threadId = _threadId ?? throw new DomainException("codex_invalid_state", "No Codex conversation is active.");
            if (_isBusy)
            {
                throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
            }
            model = _selectedModel;
            _sendingCancellation?.Dispose();
            _sendingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            sendingToken = _sendingCancellation.Token;
            _isBusy = true;
            _errorMessage = null;
            message = new()
            {
                Id = Guid.NewGuid(),
                Role = CodexMessageRole.User,
                Text = request.Prompt?.Trim() ?? "",
                CreatedAt = DateTimeOffset.Now,
                IsTranscribing = files.Any(f => f.ContentType.StartsWith("audio/", StringComparison.Ordinal)),
                Attachments = files.Select(f => new CodexAttachmentInfo { Name = f.Name, ContentType = f.ContentType, Size = f.Size }).ToArray()
            };
            _messages.Add(message);
        }
        NotifyChanged();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sendingToken);
        await SendPreparedAsync(client, threadId, model, message, files, request.SpeechLanguage, operation.Token);
    }

    private async Task SendPreparedAsync(ICodexAppServerClient client, string threadId, string model,
        CodexChatMessage message, CodexAttachmentFile[] files, string? speechLanguage, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = message.Text;
            foreach (var audio in files.Where(f => f.ContentType.StartsWith("audio/", StringComparison.Ordinal)))
            {
                var result = await _rpcInvoker.InvokeAsync<string>("speech.transcribe", new CodexTranscribeRequest
                {
                    Audio = new MobileAttachment { Name = audio.Name, ContentType = audio.ContentType, DataBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(audio.Path, cancellationToken)) },
                    Language = speechLanguage
                }, Context.Empty, cancellationToken);
                if (!result.IsSuccess)
                {
                    throw new DomainException(result.ErrorCode, result.Message);
                }

                var transcript = result.Value;
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    throw new DomainException("codex_speech_empty", "No speech was recognized. Try recording again.");
                }

                prompt = string.IsNullOrEmpty(prompt) ? transcript : prompt + "\n" + transcript;
            }
            lock (_sync)
            {
                if (_client != client || _threadId != threadId)
                {
                    return;
                }
                message.Text = prompt;
                message.IsTranscribing = false;
            }
            NotifyChanged();
            await client.StartMediaTurnAsync(threadId, prompt, model,
                files.Where(f => !f.ContentType.StartsWith("audio/", StringComparison.Ordinal)).ToArray(), cancellationToken);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (_client == client && _threadId == threadId)
                {
                    _isBusy = false;
                    message.IsTranscribing = false;
                    _errorMessage = exception.Message;
                }
            }
            NotifyChanged();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? automaticConnection;
        lock (_sync)
        {
            _sendingCancellation?.Cancel();
            _automaticConnectionSuppressed = true;
            automaticConnection = _automaticConnection;
        }
        if (automaticConnection is not null)
        {
            await automaticConnection.CancelAsync().ConfigureAwait(false);
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ICodexAppServerClient? client;
            lock (_sync)
            {
                client = _client;
                _client = null;
                _threadId = null;
                _isBusy = false;
                _connectionState = CodexConnectionState.Disconnected;
                _errorMessage = null;
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
        Task? automaticTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _automaticConnectionSuppressed = true;
            automaticTask = _automaticConnectionTask;
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (automaticTask is not null)
        {
            await automaticTask.ConfigureAwait(false);
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _automaticConnection?.Dispose();
        _lifetime.Dispose();
        _operationGate.Dispose();
        _sendingCancellation?.Dispose();
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
                    _errorMessage = CodexErrorMessages.Normalize(
                        parameters.TryGetProperty("message", out var message)
                            ? message.GetString()
                            : parameters.GetRawText());
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

        NotifyChanged();
    }

    private async Task<CodexDynamicToolResult> HandleDynamicToolCallAsync(
        CodexDynamicToolCall call,
        CancellationToken cancellationToken)
    {
        if (call.Namespace is not null
            && !string.Equals(call.Namespace, QuantumRpcToolCatalog.Namespace, StringComparison.Ordinal))
        {
            return new CodexDynamicToolResult
            {
                Success = false,
                Text = $"Unknown tool namespace '{call.Namespace}'."
            };
        }

        QuantumRpcTool? tool;
        lock (_sync)
        {
            tool = _tools.FirstOrDefault(candidate =>
                string.Equals(candidate.ToolName, call.Tool, StringComparison.Ordinal));
        }

        if (tool is null)
        {
            return new CodexDynamicToolResult
            {
                Success = false,
                Text = $"Unknown Quantum tool '{call.Tool}'."
            };
        }

        if (!_activeToolCalls.TryAdd(call.CallId, 0))
        {
            return new CodexDynamicToolResult
            {
                Success = false,
                Text = "A duplicate Quantum RPC call id was received."
            };
        }

        try
        {
            var result = await InvokeQuantumRpcAsync(tool, call.Arguments, cancellationToken)
                .ConfigureAwait(false);
            AddToolMessage($"Quantum RPC {tool.RpcName}: {(result.Success ? "completed" : "failed")}.");
            return result;
        }
        finally
        {
            _activeToolCalls.TryRemove(call.CallId, out _);
        }
    }

    private async Task<CodexDynamicToolResult> InvokeQuantumRpcAsync(
        QuantumRpcTool tool,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (tool.ReturnsValue)
        {
            var result = await _rpcInvoker.InvokeAsync<JsonElement>(
                tool.RpcName,
                arguments,
                Context.Empty,
                cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? new CodexDynamicToolResult
                {
                    Success = true,
                    Text = result.Value.GetRawText()
                }
                : Failure(result.ErrorCode, result.Message);
        }

        var emptyResult = await _rpcInvoker.InvokeAsync(
            tool.RpcName,
            arguments,
            Context.Empty,
            cancellationToken).ConfigureAwait(false);
        return emptyResult.IsSuccess
            ? new CodexDynamicToolResult
            {
                Success = true,
                Text = "{\"isSuccess\":true}"
            }
            : Failure(emptyResult.ErrorCode, emptyResult.Message);
    }

    private static CodexDynamicToolResult Failure(string errorCode, string message)
        => new()
        {
            Success = false,
            Text = JsonSerializer.Serialize(new
            {
                isSuccess = false,
                errorCode,
                message
            })
        };

    internal static string ResolveModel(
        string model,
        IReadOnlyList<CodexModelOption> availableModels)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DomainException("codex_invalid_request", "Model is required.");
        }
        var match = availableModels.FirstOrDefault(option =>
            string.Equals(option.Id, model.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Id
            ?? throw new DomainException("codex_invalid_state", $"Codex model '{model}' is not available.");
    }

    private void AppendAssistantDelta(string delta)
    {
        var index = _messages.FindLastIndex(static message =>
            message.Role == CodexMessageRole.Assistant && message.IsStreaming);
        if (index < 0)
        {
            _messages.Add(new CodexChatMessage
            {
                Id = Guid.NewGuid(),
                Role = CodexMessageRole.Assistant,
                Text = delta,
                CreatedAt = DateTimeOffset.Now,
                IsStreaming = true
            });
            return;
        }

        _messages[index] = _messages[index] with
        {
            Text = _messages[index].Text + delta
        };
    }

    private void CompleteStreamingMessage()
    {
        var index = _messages.FindLastIndex(static message => message.IsStreaming);
        if (index >= 0)
        {
            _messages[index] = _messages[index] with
            {
                IsStreaming = false
            };
        }
    }

    private void AddToolMessage(string text)
    {
        lock (_sync)
        {
            _messages.Add(new CodexChatMessage
            {
                Id = Guid.NewGuid(),
                Role = CodexMessageRole.Tool,
                Text = text,
                CreatedAt = DateTimeOffset.Now
            });
        }

        NotifyChanged();
    }

    private static string? ReadTurnError(JsonElement turn)
        => turn.TryGetProperty("error", out var error)
            && error.ValueKind != JsonValueKind.Null
            && error.TryGetProperty("message", out var message)
                ? CodexErrorMessages.Normalize(message.GetString())
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
