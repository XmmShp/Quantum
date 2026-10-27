using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex.UI.Pages;

public partial class CodexMediaInput
{
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Parameter]
    public bool Disabled
    {
        get; set;
    }
    [Parameter]
    public EventCallback<MobileAttachment[]> Changed
    {
        get; set;
    }
    [Parameter]
    public EventCallback<bool> RecordingChanged
    {
        get; set;
    }
    [Parameter] public MobileAttachment[] Value { get; set; } = [];
    private MobileAttachment[]? _previousValue;
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_previousValue, Value))
        {
            _previousValue = Value;
            _files.Clear();
            _files.AddRange(Value);
        }
    }
    private readonly List<MobileAttachment> _files = [];
    private IJSObjectReference? _module;
    private IJSObjectReference? _recordingHandle;
    private DotNetObjectReference<CodexMediaInput>? _reference;
    private bool _recording;
    private bool _starting;
    private string? _error;

    public async Task ClearAsync()
    {
        _files.Clear();
        await Changed.InvokeAsync([]);
        StateHasChanged();
    }

    private async Task PickFilesAsync(InputFileChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        _error = null;
        try
        {
            foreach (var file in args.GetMultipleFiles(4))
            {
                await using var stream = file.OpenReadStream(CodexAttachmentStore.MaximumFileBytes);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                await AddAsync(new()
                {
                    Name = file.Name,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    DataBase64 = Convert.ToBase64String(buffer.ToArray())
                });
            }
        }
        catch (Exception)
        {
            _error = "Could not add attachment. Use up to four files, 8 MiB each and 16 MiB total.";
        }
    }

    private async Task AddAsync(MobileAttachment attachment)
    {
        var total = _files.Sum(f => (long)Convert.FromBase64String(f.DataBase64).Length) + Convert.FromBase64String(attachment.DataBase64).Length;
        if (_files.Count >= 4 || total > 16 * 1024 * 1024 || attachment.DataBase64.Length > ((CodexAttachmentStore.MaximumFileBytes + 2) / 3) * 4)
        {
            throw new ArgumentException("Attachment limit exceeded.");
        }
        _files.Add(attachment);
        await Changed.InvokeAsync(_files.ToArray());
    }

    private async Task RemoveAsync(MobileAttachment attachment)
    {
        _files.Remove(attachment);
        await Changed.InvokeAsync(_files.ToArray());
    }

    private async Task ToggleRecordingAsync()
    {
        _error = null;
        try
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/quantum.plugin.codex/codex-media.js");
            if (_recording)
            {
                if (_recordingHandle is not null)
                {
                    await _recordingHandle.InvokeVoidAsync("stop");
                }
            }
            else
            {
                if (_files.Count >= 4)
                {
                    throw new InvalidOperationException("Attachment limit reached.");
                }

                if (_recordingHandle is not null)
                {
                    await _recordingHandle.DisposeAsync();
                }

                _starting = true;
                _recording = true;
                await RecordingChanged.InvokeAsync(true);
                _reference ??= DotNetObjectReference.Create(this);
                _recordingHandle = await _module.InvokeAsync<IJSObjectReference>("record", _reference);
            }
        }
        catch (Exception)
        {
            _recording = false;
            await RecordingChanged.InvokeAsync(false);
            _error = "Recording unavailable. Check microphone permission.";
        }
        finally
        {
            _starting = false;
        }
    }

    [JSInvokable]
    public Task RecordedAsync(string name, string contentType, string dataBase64) => InvokeAsync(async () =>
    {
        try
        {
            await AddAsync(new()
            {
                Name = name,
                ContentType = contentType,
                DataBase64 = dataBase64
            });
        }
        catch (Exception) { _error = "Could not add attachment. Use up to four files, 8 MiB each and 16 MiB total."; }
        _recording = false;
        await RecordingChanged.InvokeAsync(false);
        StateHasChanged();
    });

    [JSInvokable]
    public Task RecordingFailedAsync() => InvokeAsync(async () =>
    {
        _recording = false;
        _error = "Recording unavailable. Check microphone permission.";
        await RecordingChanged.InvokeAsync(false);
        StateHasChanged();
    });

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_recordingHandle is not null)
            {
                await _recordingHandle.InvokeVoidAsync("cancel");
                await _recordingHandle.DisposeAsync();
            }
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        _reference?.Dispose();
    }
}
