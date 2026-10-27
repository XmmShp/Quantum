using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex.UI.Pages;

public partial class CodexDrawer
{
    private readonly ICodexRpcServiceClient _codexClient;
    private readonly ICodexRuntimeEvents _codexEvents;
    private bool _disposed;
    private CodexIntegrationSnapshot _snapshot = new()
    {
        ConnectionState = CodexConnectionState.Disconnected,
        ThreadId = null,
        IsBusy = false,
        ToolCount = 0,
        WorkingDirectory = Environment.CurrentDirectory,
        Models = CodexRuntime.InitialModels,
        SelectedModel = CodexRuntime.DefaultModel,
        Messages = [],
        ErrorMessage = null
    };
    private string _workingDirectory = Environment.CurrentDirectory;
    private string _selectedModel = CodexRuntime.DefaultModel;
    private string _prompt = string.Empty;
    private string? _localError;
    private MobileAttachment[] _attachments = [];
    private CodexMediaInput? _mediaInput;
    private bool _recording;
    private bool _sending;
    private bool _isOpen;

    public CodexDrawer(ICodexRpcServiceClient codexClient, ICodexRuntimeEvents codexEvents)
    {
        _codexClient = codexClient;
        _codexEvents = codexEvents;
    }

    private bool IsOnCodexPage => string.Equals(
        new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/'),
        "/plugins/codex",
        StringComparison.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        _codexEvents.Changed += HandleChanged;
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
            await RunAsync(async () => ApplySnapshot(await _codexClient.EnsureAutoConnectedAsync(new(), Context.Empty)));
        }
    }

    private bool CanSend => !_sending && !_recording && !_snapshot.IsBusy && (!string.IsNullOrWhiteSpace(_prompt) || _attachments.Length > 0);

    private void OpenDrawer() => _isOpen = true;
    private void CloseDrawer() => _isOpen = false;

    private void HandleDrawerKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDrawer();
        }
    }

    private Task ConnectAsync() => RunAsync(async () => ApplySnapshot(await _codexClient.ConnectAsync(new()
    {
        WorkingDirectory = _workingDirectory,
        Model = _selectedModel
    }, Context.Empty)));
    private Task NewThreadAsync() => RunAsync(async () => ApplySnapshot(await _codexClient.StartNewThreadAsync(new()
    {
    }, Context.Empty)));

    private async Task ChangeModelAsync(ChangeEventArgs args)
    {
        var model = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(model)
            || string.Equals(model, _snapshot.SelectedModel, StringComparison.Ordinal))
        {
            return;
        }

        _selectedModel = model;
        await RunAsync(async () => ApplySnapshot(await _codexClient.ChangeModelAsync(new()
        {
            Model = model
        }, Context.Empty)));
    }

    private async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        var prompt = _prompt;
        _sending = true;
        try
        {
            await RunAsync(async () =>
            {
                var result = await _codexClient.SendAsync(new()
                {
                    Prompt = prompt,
                    Attachments = _attachments,
                    SpeechLanguage = System.Globalization.CultureInfo.CurrentUICulture.Name
                }, Context.Empty);
                ApplySnapshot(result);
                if (result.IsSuccess)
                {
                    if (_prompt == prompt)
                    {
                        _prompt = string.Empty;
                    }

                    _attachments = [];
                    if (_mediaInput is not null)
                    {
                        await _mediaInput.ClearAsync();
                    }
                }
            });
        }
        finally
        {
            _sending = false;
        }
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _localError = exception.Message;
        }
    }

    private async Task RefreshSnapshotAsync()
        => ApplySnapshot(await _codexClient.GetSnapshotAsync(new(), Context.Empty));

    private void ApplySnapshot(Result<CodexIntegrationSnapshot> result)
    {
        if (!result.IsSuccess)
        {
            _localError = result.Message;
            _selectedModel = _snapshot.SelectedModel;
            return;
        }

        var snapshot = result.Value!;
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
        _codexEvents.Changed -= HandleChanged;
        Navigation.LocationChanged -= HandleLocationChanged;
    }
}
