using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

internal sealed class AttachmentPickerView : VerticalStackLayout
{
    private readonly List<BridgeAttachment> _files = [];
    private readonly VerticalStackLayout _items = new() { Spacing = 4 };
    private readonly Label _error = new() { TextColor = MobileTheme.Error, FontSize = 12, IsVisible = false };
    private readonly Button _record;
    private NativeAudioRecorder? _recorder;
    private bool _busy;
    public event Action<BridgeAttachment[]>? Changed;
    public event Action? RecordingChanged;
    public bool IsRecording => _recorder is not null;
    private static bool Chinese => Preferences.Default.Get("quantum.mobile.language", "zh").StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    private static string T(string zh, string en) => Chinese ? zh : en;

    public AttachmentPickerView()
    {
        Spacing = 6;
        var pick = MobileTheme.PrimaryButton(T("＋ 附件", "+ Attach"));
        _record = MobileTheme.PrimaryButton(T("● 录音", "● Record"));
        pick.Clicked += async (_, _) => await RunAsync(async () =>
        {
            var files = await FilePicker.Default.PickMultipleAsync();
            if (files is null) return;
            foreach (var file in files)
            {
                if (file is null) continue;
                await using var stream = await file.OpenReadAsync();
                Add(await OutgoingAttachmentStore.SaveAsync(stream, file.FileName, string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType));
            }
        });
        _record.Clicked += async (_, _) => await RunAsync(ToggleRecordingAsync);
        var row = new HorizontalStackLayout { Spacing = 8, Children = { pick, _record } };
        Children.Add(row); Children.Add(_items); Children.Add(_error);
        Unloaded += async (_, _) => { if (_recorder is { } recorder) { _recorder = null; try { await recorder.DisposeAsync(); } catch (Exception) { } } };
    }

    private void Add(BridgeAttachment file)
    {
        if (_files.Count >= MobileAttachments.MaximumFiles || _files.Sum(f => f.Size) + file.Size > MobileAttachments.MaximumTotalBytes)
            throw new BridgeException("mobile_attachment_invalid", "Attachment limits exceeded.");
        _files.Add(file); Render(); Changed?.Invoke(_files.ToArray());
    }

    public void SetFiles(BridgeAttachment[] files)
    {
        _files.Clear(); _files.AddRange(files); Render(); Changed?.Invoke(_files.ToArray());
    }

    private void Render()
    {
        _items.Children.Clear();
        foreach (var file in _files)
        {
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
            row.Add(new Label { Text = file.Name, FontSize = 12, TextColor = MobileTheme.Muted, LineBreakMode = LineBreakMode.MiddleTruncation, VerticalOptions = LayoutOptions.Center });
            var remove = new Button { Text = "×", WidthRequest = 36, HeightRequest = 36, Padding = 0, BackgroundColor = Colors.Transparent, TextColor = MobileTheme.Muted };
            remove.Clicked += (_, _) => { _files.Remove(file); Render(); Changed?.Invoke(_files.ToArray()); };
            row.Add(remove, 1); _items.Children.Add(row);
        }
    }

    public Task StopForBackgroundAsync() => _recorder is null ? Task.CompletedTask : RunAsync(ToggleRecordingAsync);

    private async Task ToggleRecordingAsync()
    {
        if (_recorder is { } recorder)
        {
            try { Add(await recorder.StopAsync()); }
            finally { _recorder = null; RecordingChanged?.Invoke(); await recorder.DisposeAsync(); _record.Text = T("● 录音", "● Record"); }
        }
        else
        {
            if (_files.Count >= MobileAttachments.MaximumFiles) throw new InvalidOperationException("Attachment limit reached.");
            var recording = new NativeAudioRecorder();
            _recorder = recording; RecordingChanged?.Invoke();
            try { await recording.StartAsync(); }
            catch { _recorder = null; RecordingChanged?.Invoke(); await recording.DisposeAsync(); throw; }
            _record.Text = T("■ 停止录音", "■ Stop recording");
            _ = StopAfterLimitAsync(recording);
        }
    }

    private async Task StopAfterLimitAsync(NativeAudioRecorder recorder)
    {
        await Task.Delay(TimeSpan.FromSeconds(45));
        if (ReferenceEquals(_recorder, recorder)) await RunAsync(ToggleRecordingAsync);
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true; _error.IsVisible = false;
        try { await operation(); }
        catch (Exception) { _error.Text = T("附件处理失败，请检查文件大小或麦克风权限后重试。", "Could not add attachment. Check file size or microphone permission."); _error.IsVisible = true; }
        finally { _busy = false; }
    }
}
