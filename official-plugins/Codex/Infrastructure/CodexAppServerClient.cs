using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Quantum.OfficialPlugins.Codex.Application;

namespace Quantum.OfficialPlugins.Codex.Infrastructure;

internal sealed class CodexAppServerClient(ILogger<CodexAppServerClient> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _readTask;
    private Task? _errorTask;
    private long _nextRequestId;
    private bool _disposed;

    public event Func<string, JsonElement, Task>? NotificationReceived;

    public event Action<Exception?>? ConnectionClosed;

    public event Func<CodexDynamicToolCall, CancellationToken, Task<CodexDynamicToolResult>>?
        DynamicToolCallReceived;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not null)
        {
            return;
        }

        var process = new Process { StartInfo = CreateStartInfo(), EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The Codex app-server process could not be started.");
        }

        _process = process;
        _readTask = ReadLoopAsync(process.StandardOutput, _lifetime.Token);
        _errorTask = ReadErrorsAsync(process.StandardError, _lifetime.Token);
        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "quantum_desktop",
                    title = "Quantum Codex Plugin",
                    version = "0.1.0"
                },
                capabilities = new { experimentalApi = true }
            },
            cancellationToken).ConfigureAwait(false);
        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> StartThreadAsync(
        string workingDirectory,
        JsonArray dynamicTools,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            "thread/start",
            new JsonObject
            {
                ["cwd"] = workingDirectory,
                ["approvalPolicy"] = "never",
                ["sandbox"] = "read-only",
                ["serviceName"] = "quantum_desktop",
                ["dynamicTools"] = dynamicTools
            },
            cancellationToken).ConfigureAwait(false);
        return response.GetProperty("thread").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Codex did not return a thread id.");
    }

    public Task<JsonElement> StartTurnAsync(
        string threadId,
        string prompt,
        CancellationToken cancellationToken)
        => SendRequestAsync(
            "turn/start",
            new
            {
                threadId,
                input = new[] { new { type = "text", text = prompt } }
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_process is { HasExited: false } process)
        {
            try
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(1500))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        foreach (var request in _pending.Values)
        {
            request.TrySetException(new ObjectDisposedException(nameof(CodexAppServerClient)));
        }

        await AwaitBackgroundTaskAsync(_readTask).ConfigureAwait(false);
        await AwaitBackgroundTaskAsync(_errorTask).ConfigureAwait(false);
        _process?.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Could not register a Codex request.");
        }

        try
        {
            await WriteAsync(new { id = long.Parse(id), method, @params = parameters }, cancellationToken)
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
        => WriteAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("Codex is not connected.");
        var line = JsonSerializer.Serialize(message);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty("id", out var idElement)
                    && message.TryGetProperty("method", out var requestMethod))
                {
                    _ = HandleServerRequestAsync(
                        idElement.Clone(),
                        requestMethod.GetString() ?? string.Empty,
                        message.GetProperty("params").Clone(),
                        cancellationToken);
                    continue;
                }

                if (message.TryGetProperty("id", out idElement))
                {
                    CompleteRequest(idElement, message);
                    continue;
                }

                if (message.TryGetProperty("method", out var methodElement))
                {
                    var handler = NotificationReceived;
                    if (handler is not null)
                    {
                        await handler(
                            methodElement.GetString() ?? string.Empty,
                            message.TryGetProperty("params", out var parameters)
                                ? parameters.Clone()
                                : default).ConfigureAwait(false);
                    }
                }
            }

            var exception = new InvalidOperationException("The Codex app-server connection closed.");
            FailPending(exception);
            if (!cancellationToken.IsCancellationRequested)
            {
                ConnectionClosed?.Invoke(exception);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The Codex app-server output stream failed.");
            FailPending(exception);
            ConnectionClosed?.Invoke(exception);
        }
    }

    private void CompleteRequest(JsonElement idElement, JsonElement message)
    {
        var id = idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()!
            : idElement.GetRawText();
        if (!_pending.TryGetValue(id, out var completion))
        {
            return;
        }

        if (message.TryGetProperty("error", out var error))
        {
            completion.TrySetException(new InvalidOperationException(
                error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : error.GetRawText()));
            return;
        }

        completion.TrySetResult(message.GetProperty("result").Clone());
    }

    private async Task HandleServerRequestAsync(
        JsonElement id,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (method != "item/tool/call" || DynamicToolCallReceived is not { } handler)
            {
                await WriteAsync(new
                {
                    id,
                    error = new { code = -32601, message = $"Unsupported Codex request '{method}'." }
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var call = new CodexDynamicToolCall(
                parameters.GetProperty("callId").GetString()!,
                parameters.TryGetProperty("namespace", out var namespaceElement)
                    && namespaceElement.ValueKind != JsonValueKind.Null
                    ? namespaceElement.GetString()
                    : null,
                parameters.GetProperty("tool").GetString()!,
                parameters.GetProperty("arguments").Clone());
            var result = await handler(call, cancellationToken).ConfigureAwait(false);
            await WriteAsync(new
            {
                id,
                result = new
                {
                    contentItems = new[] { new { type = "inputText", text = result.Text } },
                    success = result.Success
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await WriteAsync(new
            {
                id,
                error = new { code = -32000, message = exception.Message }
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ReadErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                logger.LogDebug("Codex app-server: {CodexDiagnostic}", line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var configuredCommand = Environment.GetEnvironmentVariable("QUANTUM_CODEX_COMMAND");
        var command = "codex";
        if (!string.IsNullOrWhiteSpace(configuredCommand))
        {
            command = Path.GetFullPath(configuredCommand.Trim());
            if (!File.Exists(command))
            {
                throw new FileNotFoundException(
                    "QUANTUM_CODEX_COMMAND must point to an existing Codex executable or launcher.",
                    command);
            }
        }

        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /s /c \"\"{command}\" app-server\""
            };
        }
        else
        {
            info = new ProcessStartInfo { FileName = command };
            info.ArgumentList.Add("app-server");
        }

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        return info;
    }

    private static async Task AwaitBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
