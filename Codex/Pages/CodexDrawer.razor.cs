using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.OfficialPlugins.Codex.Infrastructure;

namespace Quantum.OfficialPlugins.Codex.Pages;

public partial class CodexDrawer
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
    private bool _isOpen;

    public CodexDrawer(ICodexRpcServiceClient codexClient, ICodexRuntimeEvents codexEvents)
    {
        CodexClient = codexClient;
        CodexEvents = codexEvents;
    }

    private bool IsOnCodexPage => string.Equals(
        new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/'),
        "/plugins/codex",
        StringComparison.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        CodexEvents.Changed += HandleChanged;
        await RunAsync(RefreshSnapshotAsync);
        _workingDirectory = _snapshot.WorkingDirectory;
        _selectedModel = _snapshot.SelectedModel;
        Navigation.LocationChanged += HandleLocationChanged;
    }

    private bool IsConnecting => _snapshot.ConnectionState == CodexConnectionState.Connecting;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.EnsureAutoConnectedAsync(new(), Context.Empty))));
        }
    }
    private bool CanSend => !_snapshot.IsBusy && !string.IsNullOrWhiteSpace(_prompt);

    private void OpenDrawer() => _isOpen = true;
    private void CloseDrawer() => _isOpen = false;

    private void HandleDrawerKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDrawer();
        }
    }

    private Task ConnectAsync() => RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.ConnectAsync(new(_workingDirectory, _selectedModel), Context.Empty))));
    private Task NewThreadAsync() => RunAsync(async () => ApplySnapshot(RpcResults.ValueOrThrow(await CodexClient.StartNewThreadAsync(new(), Context.Empty))));

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

    private async Task HandleComposerKeyDownAsync(KeyboardEventArgs args)
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

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _isOpen = false;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        _disposed = true;
        CodexEvents.Changed -= HandleChanged;
        Navigation.LocationChanged -= HandleLocationChanged;
    }
}
