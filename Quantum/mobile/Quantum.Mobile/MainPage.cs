using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Quantum.Mobile.Internal;
using MobileUi = Quantum.Mobile.Internal.Mobile;

namespace Quantum.MobileApp;

public sealed partial class MainPage : ContentPage

{

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private string _baseUrl = Preferences.Default.Get("quantum.mobile.platform", PlatformAddress.DefaultUrl);

    private Entry _email = new() { Keyboard = Keyboard.Email };

    private Entry _password = new() { IsPassword = true };

    private readonly Label _status = new() { FontSize = 13 };

    private readonly Label _desktopStatus = new() { FontSize = 12 };

    private readonly VerticalStackLayout _body = new() { Spacing = 16 };

    private readonly VerticalStackLayout _pageContent = new() { Spacing = 16 };

    private readonly VerticalStackLayout _notice = new() { Spacing = 8, IsVisible = false };
    private readonly RefreshView _viewport = new();
    private readonly Dictionary<string, BridgeSnapshot> _pageCache = new();
    private View _signInHeader = null!;
    private DateTimeOffset _subscriptionRenewAt;

    private bool _awaitingSnapshot;

    private readonly SemaphoreSlim _pollGate = new(1);

    private BridgeClient? _client;

    private PlatformSession? _authentication;

    private string _storageKey = "";

    private string? _desktopId;

    private string? _pendingId;

    private BridgeSnapshot? _snapshot;

    private MobileRenderer? _renderer;

    private CancellationTokenSource? _polling;

    private Task? _pollTask;

    private bool _preview;

    private bool _connected;

    private bool _uncertain;

    private string? _openRequestId;

    private string? _lastOutcomeKey;

    private static string T(string zh, string en) => Chinese ? zh : en;
    private static bool Chinese => Preferences.Default.Get("quantum.mobile.language", CultureInfo.CurrentUICulture.Name).StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public MainPage()
    {
        ApplyLanguage(Preferences.Default.Get("quantum.mobile.language", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
        Title = "Quantum Mobile";
        BackgroundColor = MobileTheme.Canvas;
        NavigationPage.SetHasNavigationBar(this, false);
        var root = new VerticalStackLayout
        {
            Padding = new Thickness(18, 12),
            Spacing = 16,
            MaximumWidthRequest = 480,
            HorizontalOptions = LayoutOptions.Fill
        };
        _status.TextColor = MobileTheme.Muted;
        _desktopStatus.TextColor = MobileTheme.Muted;
        _notice.Children.Add(_noticeText);
        _reviewPending = Button(T("核对桌面后清除待确认状态", "Clear pending state after checking desktop"), ConfirmPendingAsync);
        _dismissNotice = Button(T("关闭提示", "Dismiss"), () => { _notice.IsVisible = false; return Task.CompletedTask; });
        _notice.Children.Add(_reviewPending);
        _notice.Children.Add(_dismissNotice);
        root.Children.Add(_actionFeedback);
        root.Children.Add(_notice);
        root.Children.Add(_body);
        _signInHeader = CreateHeader();
        _viewport.Content = new ScrollView { Content = root };
        _viewport.RefreshColor = MobileTheme.Accent;
        _viewport.Refreshing += async (_, _) => await RefreshFromPullAsync();
        var shell = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        shell.Add(_viewport, 0, 1);

        BuildNavigation(shell);
        BuildAccountMenu(shell);
        Content = shell;
        ShowSignIn();
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = MobileTheme.PrimaryButton(text);
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                if (_isSignIn)
                {
                    _signInError.Text = SignInError(exception);
                    _signInError.IsVisible = true;
                }
                else
                {
                    _status.Text = FriendlyError(exception);
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private async Task SignInAsync()

    {

        var uri = PlatformAddress.Parse(_baseUrl);

        _authentication = CreateAuthentication(uri);
        try { await _authentication.LoginAsync(_email.Text ?? "", _password.Text ?? ""); }
        finally { _password.Text = ""; }
        _accountAccepted = true;
        await ConnectSessionAsync(uri);
    }

    private async Task ConnectSessionAsync(Uri uri)
    {
        var userId = _authentication!.User!.UserId;
        _storageKey = "quantum.mobile." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri + userId)));
        _client = new(_http, uri, () => _authentication.AccessToken) { Authentication = _authentication };
        var instance = Preferences.Default.Get(_storageKey + ".instance", "");
        if (instance.Length == 0) { instance = Guid.NewGuid().ToString("N"); Preferences.Default.Set(_storageKey + ".instance", instance); }
        var saved = await SecureStorage.Default.GetAsync(_storageKey);

        if (saved is not null)

        {

            _client.Registration = JsonSerializer.Deserialize<BridgeRegistration>(saved, MobileJson.Options);

            try { await _client.CallAsync<JsonElement>("heartbeat", new { clientInstanceId = instance }); }

            catch (BridgeException e) when (e.Code == "mobile_device_revoked") { _client.Registration = null; }

        }

        if (_client.Registration is null)

        {

            _client.Registration = await _client.CallAsync<BridgeRegistration>("register", new { name = DeviceInfo.Current.Name, kind = "mobile", clientInstanceId = instance });

            await SecureStorage.Default.SetAsync(_storageKey, JsonSerializer.Serialize(_client.Registration, MobileJson.Options));

        }

        _pendingId = Preferences.Default.Get(_storageKey + ".pending", (string?)null);
        var queued = await SecureStorage.Default.GetAsync(_storageKey + ".outbox");
        _outbox = queued is null ? null : JsonSerializer.Deserialize<PendingAction>(queued, MobileJson.Options);
        if (_outbox is not null) { _pendingId = _outbox.Send.Id; SetActionFeedback(T("正在恢复待发送操作…", "Recovering pending action…")); }

        var failed = await SecureStorage.Default.GetAsync(_storageKey + ".failed-draft");
        _failedDraft = failed is null ? null : JsonSerializer.Deserialize<PendingAction>(failed, MobileJson.Options);
        _offerFailedDraft = _failedDraft is not null;
        if (_failedDraft is not null) EnsureDraftRecovery();
        _preview = false;
        Preferences.Default.Set("quantum.mobile.email", _email.Text?.Trim() ?? "");

        await EnterDesktopAsync();

        Resume();

    }

    private async Task ShowPagesAsync()
    {
        _language.IsEnabled = true;
        var pages = await _client!.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId, includeIcons = true, includeIconImages = true });
        _navigation.Replace(pages);
        RenderNavigation();
        if (pages.Length > 0)
        {
            var page = _navigation.Current ?? pages[0];
            await SelectNavigationPageAsync(page);
        }
        else
        {
            _snapshot = null; _renderer = null; _body.Children.Clear();
            _body.Children.Add(new Label { Text = T("这台电脑暂时没有移动插件页面", "No mobile plugin pages on this desktop yet"), FontSize = 24 });
            _body.Children.Add(MobileTheme.Caption(T("安装支持移动页面的插件后，可从设置中刷新插件列表。", "Install a plugin with mobile pages, then refresh the plugin list in Settings.")));
        }
    }

    private async Task OpenAsync(string pluginId, string pageId)

    {

        if (_renderer?.IsDirty == true && !await DisplayAlertAsync(T("刷新页面", "Refresh page"), T("丢弃未提交的输入？", "Discard unsubmitted input?"), T("刷新", "Refresh"), T("取消", "Cancel"))) return;
        if (_navigation.Pages.Any(p => p.PluginId == pluginId && p.PageId == pageId)) _navigation.Select(pluginId, pageId);
        await CloseNavigationAsync();
        RenderNavigation();

        _pageContent.IsEnabled = false;
        _awaitingSnapshot = true;
        _snapshot = null; _renderer = null; _deferredSnapshot = null;
        _body.Children.Clear();
        if (_pageCache.TryGetValue(pluginId + ":" + pageId, out var cached))
        {
            RenderSnapshot(cached, false);
            _pageContent.IsEnabled = false;
        }
        else
        {
            _body.Children.Add(new ActivityIndicator { IsRunning = true, Color = MobileTheme.Accent, HeightRequest = 32 });
        }
        await RequestSnapshotAsync(pluginId, pageId);
    }

    private async Task RequestSnapshotAsync(string pluginId, string pageId)
    {
        _openRequestId = Guid.NewGuid().ToString("N");
        _subscriptionRenewAt = DateTimeOffset.UtcNow.AddSeconds(_awaitingSnapshot ? 10 : 120);
        _status.Text = T("正在同步…", "Syncing…");
        await _client!.CallAsync<JsonElement>("send", new BridgeSend(_openRequestId, _desktopId!, "open", MobileJson.Element(new BridgeOpen(pluginId, pageId))));
    }

    private void ShowSnapshot(BridgeSnapshot snapshot)

    {
        if (_snapshot?.ViewId == snapshot.ViewId) return;
        if (!_preview && snapshot.RequestId != _openRequestId) return;
        if (_snapshot is not null && snapshot.RequestId == _snapshot.RequestId && snapshot.ExpiresAt < _snapshot.ExpiresAt) return;
        if (_pendingId is not null) { _deferredSnapshot = snapshot; return; }
        if (_awaitingSnapshot) _subscriptionRenewAt = DateTimeOffset.UtcNow.AddMinutes(2);
        _awaitingSnapshot = false;
        RenderSnapshot(snapshot, true);
        _refreshCompletion?.TrySetResult();
    }

    private BridgeSnapshot? _deferredSnapshot;

    private void RenderSnapshot(BridgeSnapshot snapshot, bool preserveInputs)
    {
        var samePage = _snapshot?.PluginId == snapshot.PluginId && _snapshot.Page.Id == snapshot.Page.Id;
        _snapshot = snapshot;
        _pageCache[snapshot.PluginId + ":" + snapshot.Page.Id] = snapshot;
        _status.Text = MobileFeedback.Snapshot(_uncertain, _pendingId is not null, Chinese);
        if (samePage && preserveInputs && _renderer is not null)
        {
            var root = _renderer.Update(snapshot.Page);
            if (!ReferenceEquals(_pageContent.Children[0], root))
            {
                _pageContent.Children.Clear();
                _pageContent.Children.Add(root);
            }
        }
        else
        {
            _renderer = new(InvokeAsync);
            _pageContent.Children.Clear();
            _pageContent.Children.Add(_renderer.Render(snapshot.Page));
            _body.Children.Clear();
            _body.Children.Add(_pageContent);
        }
        UpdateActionAvailability();
        if (_offerFailedDraft && _failedDraft is { } draft && snapshot.PluginId == draft.PluginId && snapshot.Page.Id == draft.PageId)
        {
            _offerFailedDraft = false;
            ShowNotice(T("上次未完成的输入已保留，可恢复后继续编辑。", "Your unsuccessful submission was saved. Restore it to continue editing."));
        }
    }

    private async Task ConfirmPendingAsync()
    {
        if (_uncertain && await DisplayAlertAsync(T("确认业务结果", "Confirm the outcome"),
            T("请先在桌面检查操作是否已经生效。", "Check whether the operation already took effect on desktop."),
            T("已核对", "Checked"), T("取消", "Cancel")))
        {
            ClearPending(); _uncertain = false; _notice.IsVisible = false;
            if (_snapshot is { } snapshot) await RequestSnapshotAsync(snapshot.PluginId, snapshot.Page.Id);
        }
    }

    private async Task InvokeAsync(string actionId, Dictionary<string, JsonElement> inputs)

    {

        if (_preview)

        {

            _status.Text = "MOCK · " + JsonSerializer.Serialize(inputs);
            if (_pendingId is not null) return;
            _pendingId = Guid.NewGuid().ToString("N");
            _outbox = new(new BridgeSend(_pendingId, "mock", "action", MobileJson.Element(new { })),
                "mock", _snapshot!.PluginId, _snapshot.Page.Id, _snapshot.ExpiresAt, inputs);
            _renderer?.ResetSubmittedInputs(inputs);
            SetActionFeedback(T("待发送…", "Queued…"));
            UpdateActionAvailability();
            return;

        }

        if (_savingAction || _uncertain || _pendingId is not null || _awaitingSnapshot || _snapshot is null || _snapshot.ExpiresAt <= _client!.UtcNow)
        { ShowNotice(T("请先刷新页面或确认之前的结果", "Refresh the page or check the previous result first")); return; }
        var id = Guid.NewGuid().ToString("N");
        var pending = new PendingAction(new BridgeSend(id, _desktopId!, "action",
            MobileJson.Element(new BridgeInvoke(_snapshot.ViewId, actionId, inputs))),
            _client.Registration!.Device.Id, _snapshot.PluginId, _snapshot.Page.Id, _snapshot.ExpiresAt, inputs);
        _savingAction = true;
        SetActionFeedback(T("正在保存…", "Saving…"));
        UpdateActionAvailability();
        try
        {
            // Persist the complete immutable command before acknowledging the tap or touching inputs.
            await SecureStorage.Default.SetAsync(_storageKey + ".outbox", JsonSerializer.Serialize(pending, MobileJson.Options));
            _outbox = pending; _pendingId = id;
            _renderer?.ResetSubmittedInputs(inputs);
            SetActionFeedback(T("待发送…", "Queued…"));
            UpdateActionAvailability();
            _ = FlushOutboxAsync(CancellationToken.None);
        }
        catch (Exception) { ShowNotice(T("无法保存操作，请重试", "Could not save the action. Please try again.")); }
        finally { _savingAction = false; UpdateActionAvailability(); }
    }

    private void ClearPending()
    {
        _pendingId = null; _outbox = null;
        if (!_preview) { Preferences.Default.Remove(_storageKey + ".pending"); SecureStorage.Default.Remove(_storageKey + ".outbox"); }
    }

    private void Outcome(BridgeOutcome outcome)

    {

        if (_pendingId != outcome.RequestId) return;

        _status.Text = MobileFeedback.Outcome(outcome, Chinese);

        SetActionFeedback(_status.Text ?? "");
        if (outcome.Status is "accepted" or "running") return;

        if (_lastOutcomeKey == outcome.RequestId + outcome.Status) return;

        _lastOutcomeKey = outcome.RequestId + outcome.Status;

        _uncertain = outcome.Status is not ("succeeded" or "failed");

        if (outcome.Status == "failed") RetainFailedDraft();
        if (outcome.Status == "succeeded" && _outbox is { } completed && _failedDraft is { } failed &&
            completed.PluginId == failed.PluginId && completed.PageId == failed.PageId)
        {
            _failedDraft = null; _offerFailedDraft = false;
            if (_restoreDraft is not null) _restoreDraft.IsVisible = false;
            if (!_preview) SecureStorage.Default.Remove(_storageKey + ".failed-draft");
        }
        if (!_uncertain) ClearPending();

        UpdateActionAvailability();

        if (_uncertain || outcome.Status == "failed") ShowNotice(_status.Text ?? "");

        if (!_uncertain && _deferredSnapshot is { } next)
        {
            _deferredSnapshot = null;
            ShowSnapshot(next);
        }
        else if (!_uncertain && _snapshot is { } snapshot)
        {
            _subscriptionRenewAt = DateTimeOffset.MinValue;
        }

    }

    public void Suspend() { if (_renderer is { } renderer) _ = renderer.StopRecordingAsync(); _refreshCompletion?.TrySetCanceled(); _polling?.Cancel(); _connected = false; _pageContent.IsEnabled = false; }

    public void Resume()

    {
#if DEBUG
        if (_debugPreviewStartup) return;
#endif

        if (_preview) return;
        if (_client is null) { _ = RestoreLoginAsync(true); return; }

        _ = ResumeAsync();

    }

    private async Task ResumeAsync()

    {

        if (_pollTask is { IsCompleted: false }) { _polling?.Cancel(); await _pollTask; }

        _polling?.Dispose(); _polling = new();

        _pollTask = PollAsync(_polling.Token);

    }

    private async Task PollAsync(CancellationToken token)

    {

        if (!await _pollGate.WaitAsync(0, token)) return;

        var delay = .5;
        var nextDevices = DateTimeOffset.MinValue;
        var nextStatus = DateTimeOffset.MinValue;

        try

        {

            while (!token.IsCancellationRequested && _client is not null)

            {

                try

                {

                    var messages = await _client.CallAsync<BridgeMessage[]>("poll", cancellationToken: token);

                    foreach (var message in messages)

                    {

                        if (message.SenderId == _desktopId && message.Kind == "snapshot") ShowSnapshot(MobileJson.Read<BridgeSnapshot>(message.Body));

                        if (message.SenderId == _desktopId && message.Kind == "pageError")

                        {

                            var error = MobileJson.Read<BridgePageError>(message.Body);

                            if (error.RequestId == _openRequestId)
                            {
                                _status.Text = MobileFeedback.Failure(error.Code, Chinese);
                                ShowNotice(_status.Text ?? "");
                                _refreshCompletion?.TrySetException(new BridgeException(error.Code, _status.Text ?? "Page unavailable."));
                            }

                        }

                        if (message.Kind == "result") Outcome(MobileJson.Read<BridgeOutcome>(message.Body));

                        await _client.CallAsync<JsonElement>("ack", new BridgeAck(message.Id, message.Lease!), token);

                    }

                    if (_pendingId is not null && DateTimeOffset.UtcNow >= nextStatus)

                    {

                        nextStatus = DateTimeOffset.UtcNow.AddSeconds(2);
                        try { Outcome(await _client.CallAsync<BridgeOutcome>("status", new { requestId = _pendingId }, token)); }

                        catch (BridgeException e) when (e.Code == "mobile_not_found")

                        { if (_outbox is not null) await FlushOutboxAsync(token);
                          else { _uncertain = true; ShowNotice(T("请在电脑核对之前的操作", "Check the previous action on desktop")); } }

                    }

                    if (DateTimeOffset.UtcNow >= nextDevices || _snapshot is null && !_awaitingSnapshot && _pendingId is null && !_uncertain)

                    {

                        nextDevices = DateTimeOffset.UtcNow.AddSeconds(10);
                        var desktops = await _client.CallAsync<BridgeDevice[]>("devices", cancellationToken: token);

                        _knownDesktops = desktops.Where(d => d.Kind == "desktop").ToArray();
                        _currentDesktop = _knownDesktops.FirstOrDefault(d => d.Id == _desktopId);
                        _connected = _currentDesktop is not null && MobileDesktopSelection.IsOnline(_currentDesktop, _client.UtcNow);
                        if (_snapshot is null && !_awaitingSnapshot && !_selectingDesktop && _pendingId is null && !_uncertain)
                        {
                            var available = MobileDesktopSelection.Choose(_knownDesktops, _desktopId, _client.UtcNow, Preferences.Default.Get(_storageKey + ".desktop-name", ""));
                            if (available is not null && MobileDesktopSelection.IsOnline(available, _client.UtcNow))
                                await SelectDesktopAsync(available, false);
                        }
                        _currentDesktopState.Text = _connected ? T("在线", "Online") : T("未连接", "Not connected");
                        if (_menuExpanded) RenderDesktopChoices();

                        _desktopStatus.Text = (_connected ? T("电脑在线 · ", "Desktop online · ") : T("电脑离线 · ", "Desktop offline · ")) + DateTime.Now.ToString("HH:mm:ss");

                    }

                    if (_snapshot is { } current && _pendingId is null && !_uncertain && DateTimeOffset.UtcNow >= _subscriptionRenewAt)
                        await RequestSnapshotAsync(current.PluginId, current.Page.Id);
                    else if (_awaitingSnapshot && _navigation.Current is { } selected && DateTimeOffset.UtcNow >= _subscriptionRenewAt)
                        await RequestSnapshotAsync(selected.PluginId, selected.PageId);
                    delay = .5;

                    UpdateActionAvailability();

                }

                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (BridgeException exception) when (exception.Code == "mobile_device_revoked")
                {
                    ShowExpiredDevice();
                    break;
                }

                catch (Exception exception)

                {

                    _connected = false; UpdateActionAvailability();
                    if (_pendingId is not null) SetActionFeedback(T("连接中断，操作已保存，正在重连…", "Connection lost. Action saved; reconnecting…"));

                    _status.Text = exception is BridgeException e ? e.Code : T("连接中断，正在重连", "Connection interrupted; reconnecting");

                    delay = Math.Min(60, delay * 2);

                }

                try { await Task.Delay(TimeSpan.FromSeconds(delay), token); } catch (OperationCanceledException) { break; }

            }

        }

        finally { _pollGate.Release(); }

    }

    private async Task SignOutAsync()

    {

        Suspend();

        if (_pollTask is not null) await _pollTask;

        try { if (_client is not null) await _client.CallAsync<JsonElement>("revoke"); } catch (Exception) { }

        if (_storageKey.Length > 0) { SecureStorage.Default.Remove(_storageKey); Preferences.Default.Remove(_storageKey + ".pending"); SecureStorage.Default.Remove(_storageKey + ".outbox"); }

        if (_authentication is not null) await _authentication.LogoutAsync();
        _client = null; _snapshot = null; _renderer = null; _pendingId = null; _outbox = null; _actionFeedback.IsVisible = false; _uncertain = false; _desktopId = null; _currentDesktop = null; _knownDesktops = [];

        _failedDraft = null; if (_restoreDraft is not null) _restoreDraft.IsVisible = false;
        _status.Text = ""; _desktopStatus.Text = ""; _pageCache.Clear(); _deferredSnapshot = null; ShowSignIn();

    }

#if DEBUG
    private void Preview()

    {

        _preview = true; _status.Text = T("模拟器 · MOCK · 不执行业务", "Simulator · MOCK · No business execution");

        var page = MobileUi.Page("preview", "Quick capture", MobileUi.VStack("root",

            MobileUi.Text("title", "A thought worth keeping.").Appearance("title"),

            MobileUi.Text("subtitle", "Write it here. Your desktop takes care of the rest.").Appearance("caption"),

            MobileUi.Divider("line"), MobileUi.TextField("content").Hint("What's on your mind?").Limit(500),

            MobileUi.Toggle("pinned", "Pin this note"),
            new MobileNode("Attachments", "attachments"),

            new MobileNode("Button", "save") { Text = "Save to desktop", ActionId = "mock-save", InputIds = ["content", "pinned", "attachments"] })) with { SchemaVersion = 2 };

        RenderPreview(page);
        SetupPreviewNavigation(page);

    }

    private void RenderPreview(MobilePage page)

    {
        _isSignIn = false;
        _status.IsVisible = true;

        _preview = true; _snapshot = null;

        _status.Text = T("模拟器 · MOCK · 不执行业务", "Simulator · MOCK · No business execution");

        ShowSnapshot(new("preview", "mock", page.Id == "mock-status" ? "mock.status" : "mock", "mock", DateTimeOffset.UtcNow.AddMinutes(5), page));
        _status.Text = T("模拟器 · MOCK · 不执行业务", "Simulator · MOCK · No business execution");

        _pageContent.IsEnabled = true;

        // Preview is deliberately isolated from platform transport.

        _body.Children.Add(Button(T("退出模拟器", "Close simulator"), () => { _preview = false; _snapshot = null; ResetNavigation(); ShowSignIn(); return Task.CompletedTask; }));

    }

#if WINDOWS || MACCATALYST

    private async Task PreviewFileAsync()

    {

        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Quantum mobile UI JSON" });

        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();

        var buffer = new byte[4096];

        int count;

        while ((count = await stream.ReadAsync(buffer)) > 0)

        {

            if (memory.Length + count > MobileUiValidator.MaximumPageBytes) throw new ArgumentException("UI JSON exceeds 128 KiB.");

            memory.Write(buffer, 0, count);

        }

        var page = JsonSerializer.Deserialize<MobilePage>(memory.ToArray(), MobileJson.Options)!;

        MobileUiValidator.Validate(page);

        MobileNode Mock(MobileNode node) => node with
        {
            Action = null,

            ActionId = node.Action is null ? null : "mock-" + node.Id,

            InputIds = node.Action?.Inputs.Values.Distinct().ToArray() ?? [],
            Children = node.Children.Select(Mock).ToArray()
        };

        RenderPreview(page with { Root = Mock(page.Root) });

    }

#endif

#endif

}
