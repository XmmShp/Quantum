using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Pages;

public partial class Codex
{
    private readonly ICodexRpcServiceClient CodexClient;
    private readonly ICodexRuntimeEvents CodexEvents;
    private bool _disposed;
    private CodexIntegrationSnapshot _snapshot = new(CodexConnectionState.Disconnected, null, false, 0,
        Environment.CurrentDirectory, CodexRuntime.InitialModels, CodexRuntime.DefaultModel, [], null);
    private string _workingDirectory = Environment.CurrentDirectory;
    private string _selectedModel = CodexRuntime.DefaultModel;
    private string _prompt = string.Empty;
    private string? _localError;

    public Codex(ICodexRpcServiceClient codexClient, ICodexRuntimeEvents codexEvents)
    {
        CodexClient = codexClient;
        CodexEvents = codexEvents;
    }

    protected override async Task OnInitializedAsync()
    {
        CodexEvents.Changed += HandleChanged;
        await RunAsync(RefreshSnapshotAsync);
        _workingDirectory = _snapshot.WorkingDirectory;
        _selectedModel = _snapshot.SelectedModel;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.EnsureAutoConnectedAsync(new(), Context.Empty))));
        }
    }

    private bool IsConnecting => _snapshot.ConnectionState == CodexConnectionState.Connecting;

    private bool CanSend => !_snapshot.IsBusy && !string.IsNullOrWhiteSpace(_prompt);

    private string StateClass => _snapshot.ConnectionState.ToString().ToLowerInvariant();

    private string StateLabel => _snapshot.ConnectionState switch
    {
        CodexConnectionState.Connected => L["Connected"],
        CodexConnectionState.Connecting => L["Connecting"],
        CodexConnectionState.Failed => L["Connection failed"],
        _ => L["Disconnected"]
    };

    private async Task ConnectAsync()
        => await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.ConnectAsync(new(_workingDirectory, _selectedModel), Context.Empty))));

    private async Task DisconnectAsync() => await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.DisconnectAsync(new(), Context.Empty))));

    private async Task NewThreadAsync() => await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.StartNewThreadAsync(new(), Context.Empty))));

    private async Task ChangeModelAsync(ChangeEventArgs args)
    {
        var model = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, _snapshot.SelectedModel, StringComparison.Ordinal))
        {
            return;
        }

        _selectedModel = model;
        await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.ChangeModelAsync(new(model), Context.Empty))));
    }

    private async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        var prompt = _prompt;
        _prompt = string.Empty;
        await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.SendAsync(new(prompt), Context.Empty))));
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && (args.CtrlKey || args.MetaKey))
        {
            await SendAsync();
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _localError = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _localError = exception.Message;
        }
    }

    private async Task RefreshSnapshotAsync()
        => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.GetSnapshotAsync(new(), Context.Empty)));

    private void ApplySnapshot(CodexIntegrationSnapshot snapshot)
    {
        _snapshot = snapshot;
        _selectedModel = snapshot.SelectedModel;
    }

    private void HandleChanged()
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            if (_disposed)
            {
                return;
            }

            await RunAsync(RefreshSnapshotAsync);
            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        CodexEvents.Changed -= HandleChanged;
    }
}
