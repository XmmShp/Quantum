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
            Padding = new Thickness(24, 28),
            Spacing = 16,
            MaximumWidthRequest = 480,
            HorizontalOptions = LayoutOptions.Fill
        };
        _status.TextColor = MobileTheme.Muted;
        _desktopStatus.TextColor = MobileTheme.Muted;
        root.Children.Add(_status);
        root.Children.Add(_desktopStatus);
        root.Children.Add(_body);
        var shell = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        shell.Add(CreateHeader());
        shell.Add(new ScrollView { Content = root }, 0, 1);
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
        var saved = await SecureStorage.Default.GetAsync(_storageKey);

        if (saved is not null)

        {

            _client.Registration = JsonSerializer.Deserialize<BridgeRegistration>(saved, MobileJson.Options);

            try { await _client.CallAsync<JsonElement>("heartbeat"); }

            catch (BridgeException e) when (e.Code == "mobile_device_revoked") { _client.Registration = null; }

        }

        if (_client.Registration is null)

        {

            _client.Registration = await _client.CallAsync<BridgeRegistration>("register", new { name = DeviceInfo.Current.Name, kind = "mobile" });

            await SecureStorage.Default.SetAsync(_storageKey, JsonSerializer.Serialize(_client.Registration, MobileJson.Options));

        }

        _pendingId = Preferences.Default.Get(_storageKey + ".pending", (string?)null);

        _preview = false;
        Preferences.Default.Set("quantum.mobile.email", _email.Text?.Trim() ?? "");

        await ShowDevicesAsync();

        Resume();

    }

    private async Task ShowDevicesAsync()

    {

        _snapshot = null; _desktopId = null; _renderer = null;

        var devices = await _client!.CallAsync<BridgeDevice[]>("devices");

        var bindings = await _client.CallAsync<BridgeBinding[]>("bindings");
        if (!_uncertain && _pendingId is null) _status.Text = T("请选择已授权的电脑，或申请连接。", "Select an approved desktop or request access.");
        var online = devices.Where(d => d.Kind == "desktop" && d.SeenAt > _client.UtcNow.AddSeconds(-45))
            .OrderByDescending(d => d.SeenAt).ToArray();

        _isSignIn = false;
        _status.IsVisible = true;
        _desktopStatus.IsVisible = true;
        _language.IsEnabled = true;
        _body.Children.Clear();

        _body.Children.Add(new Label { Text = T("选择你的电脑", "Choose your desktop"), FontSize = 28 });

        _body.Children.Add(new Label
        {
            Text = T("在电脑上打开“移动访问”，共享插件页面后，再在这里选择电脑。", "Enable Mobile access and share plugin pages on your computer, then choose it here."),
            TextColor = MobileTheme.Muted,
            FontSize = 14
        });
        if (online.Length == 0)
        {
            _body.Children.Add(MobileTheme.Card(new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = T("暂时没有可连接的电脑", "No desktops available yet"), TextColor = MobileTheme.Ink, FontSize = 18, FontAttributes = FontAttributes.Bold },
                    new Label { Text = T("请确认电脑使用相同的平台账号，已开启共享并保持在线。", "Use the same platform account on your desktop, start sharing, and keep it online."), TextColor = MobileTheme.Muted, FontSize = 14 }
                }
            }));
        }
        foreach (var device in online)

        {

            var approved = bindings.Any(b => b.DesktopId == device.Id && b.Approved);

            _body.Children.Add(Button(device.Name + " · " + device.Id[..8] + " · " + (approved ? T("已授权", "Approved") : T("申请连接", "Request access")), async () =>

            {

                if (!approved)

                {

                    await _client.CallAsync<JsonElement>("bind", new { desktopId = device.Id });

                    _status.Text = T("请在桌面的“移动访问”中确认设备：", "Approve this device in desktop Mobile access: ") + _client.Registration!.Device.Id[..8];

                }

                else { _desktopId = device.Id; await ShowPagesAsync(); }

            }));

        }

        _body.Children.Add(Button(T("刷新设备", "Refresh devices"), ShowDevicesAsync));

        _body.Children.Add(Button(T("退出登录", "Sign out"), SignOutAsync));

    }

    private async Task ShowPagesAsync()

    {
        _language.IsEnabled = true;

        var pages = await _client!.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId });
        if (!_uncertain && _pendingId is null) _status.Text = T("已连接电脑，请选择共享页面。", "Connected to your desktop. Choose a shared page.");

        _snapshot = null; _renderer = null;

        _body.Children.Clear();

        _body.Children.Add(new Label { Text = T("共享的页面", "Shared pages"), FontSize = 28 });

        foreach (var page in pages)

            _body.Children.Add(Button(page.Title, () => OpenAsync(page.PluginId, page.PageId)));

        if (pages.Length == 0) _body.Children.Add(new Label { Text = T("电脑尚未共享页面", "No pages are shared") });

        _body.Children.Add(Button(T("返回设备列表", "Back to desktops"), ShowDevicesAsync));

    }

    private async Task OpenAsync(string pluginId, string pageId)

    {

        if (_renderer?.IsDirty == true && !await DisplayAlertAsync(T("刷新页面", "Refresh page"), T("丢弃未提交的输入？", "Discard unsubmitted input?"), T("刷新", "Refresh"), T("取消", "Cancel"))) return;

        _pageContent.IsEnabled = false;

        _snapshot = null; _renderer = null;

        _openRequestId = Guid.NewGuid().ToString("N");

        _status.Text = T("等待电脑生成页面…", "Waiting for desktop…");

        await _client!.CallAsync<JsonElement>("send", new BridgeSend(_openRequestId, _desktopId!, "open", MobileJson.Element(new BridgeOpen(pluginId, pageId))));

    }

    private void ShowSnapshot(BridgeSnapshot snapshot)

    {
        _language.IsEnabled = false;

        if (_snapshot?.ViewId == snapshot.ViewId) return;

        if (!_preview && snapshot.RequestId != _openRequestId) return;

        if (_snapshot is not null && _renderer?.IsDirty == true) return; // Never replace a live draft via a duplicate/reordered response.

        _snapshot = snapshot;
        _status.Text = MobileFeedback.Snapshot(_uncertain, _pendingId is not null, Chinese);

        _renderer = new(InvokeAsync);

        _pageContent.Children.Clear();

        _pageContent.Children.Add(_renderer.Render(snapshot.Page));

        _body.Children.Clear();

        _body.Children.Add(new Label { Text = snapshot.Page.Title, FontSize = 28, FontAttributes = FontAttributes.Bold });

        _body.Children.Add(_pageContent);

        _body.Children.Add(Button(T("刷新页面", "Refresh page"), async () =>

        {

            var pluginId = snapshot.PluginId; var pageId = snapshot.Page.Id;

            await OpenAsync(pluginId, pageId);

        }));

        _body.Children.Add(Button(T("返回页面列表", "Back to pages"), async () =>

        {

            if (_renderer?.IsDirty == true && !await DisplayAlertAsync(T("返回", "Go back"), T("丢弃未提交的输入？", "Discard unsubmitted input?"), T("返回", "Go back"), T("取消", "Cancel"))) return;

            await ShowPagesAsync();

        }));

        if (!_preview)

            _body.Children.Add(Button(T("核对桌面后清除待确认状态", "Clear pending state after checking desktop"), async () =>

            {

                if ((_pendingId is not null || _uncertain) && await DisplayAlertAsync(T("确认业务结果", "Confirm the outcome"),

                    T("请先在桌面检查操作是否已经生效。清除后可手动再次操作。", "Check whether the operation already took effect on desktop. Clearing permits another manual action."),

                    T("已核对", "Checked"), T("取消", "Cancel"))) { ClearPending(); _uncertain = false; }

            }));

        _pageContent.IsEnabled = !_uncertain && _pendingId is null && snapshot.ExpiresAt > (_client?.UtcNow ?? DateTimeOffset.UtcNow);

    }

    private async Task InvokeAsync(string actionId, Dictionary<string, JsonElement> inputs)

    {

        if (_preview)

        {

            _status.Text = "MOCK · " + JsonSerializer.Serialize(inputs);

            return;

        }

        try

        {

            if (_uncertain || _pendingId is not null || !_connected || _snapshot is null || _snapshot.ExpiresAt <= _client!.UtcNow)

                throw new InvalidOperationException(T("请先同步页面或确认之前的结果", "Sync the page or confirm the previous result first"));

            _pendingId = Guid.NewGuid().ToString("N");

            Preferences.Default.Set(_storageKey + ".pending", _pendingId);

            _pageContent.IsEnabled = false;

            _status.Text = T("正在提交…", "Submitting…");

            await _client!.CallAsync<JsonElement>("send", new BridgeSend(_pendingId, _desktopId!, "action", MobileJson.Element(new BridgeInvoke(_snapshot.ViewId, actionId, inputs))));

            _status.Text = T("平台已接收，等待电脑执行", "Accepted by platform; waiting for desktop");

        }

        catch (BridgeException exception)

        {

            ClearPending(); _status.Text = exception.Code;

        }

        catch (Exception)

        {

            _status.Text = T("结果待确认，正在查询；不会自动重做", "Result pending; checking status without repeating the action");

        }

    }

    private void ClearPending() { _pendingId = null; Preferences.Default.Remove(_storageKey + ".pending"); }

    private void Outcome(BridgeOutcome outcome)

    {

        if (_pendingId != outcome.RequestId) return;

        _status.Text = MobileFeedback.Outcome(outcome, Chinese);

        if (outcome.Status is "accepted" or "running") return;

        if (_lastOutcomeKey == outcome.RequestId + outcome.Status) return;

        _lastOutcomeKey = outcome.RequestId + outcome.Status;

        _uncertain = outcome.Status is not ("succeeded" or "failed");

        if (!_uncertain) ClearPending();

        _pageContent.IsEnabled = false;

        // A successful business result does not imply a new page snapshot exists. Refresh is explicit.

    }

    public void Suspend() { _polling?.Cancel(); _connected = false; _pageContent.IsEnabled = false; }

    public void Resume()

    {

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

        var delay = 2;

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

                            if (error.RequestId == _openRequestId) _status.Text = MobileFeedback.Failure(error.Code, Chinese);

                        }

                        if (message.Kind == "result") Outcome(MobileJson.Read<BridgeOutcome>(message.Body));

                        await _client.CallAsync<JsonElement>("ack", new BridgeAck(message.Id, message.Lease!), token);

                    }

                    if (_pendingId is not null)

                    {

                        try { Outcome(await _client.CallAsync<BridgeOutcome>("status", new { requestId = _pendingId }, token)); }

                        catch (BridgeException e) when (e.Code == "mobile_not_found")

                        { _status.Text = T("平台未确认该请求，请在电脑核对后重新登录", "Request not confirmed. Check desktop state before signing in again."); }

                    }

                    if (_desktopId is not null)

                    {

                        var desktops = await _client.CallAsync<BridgeDevice[]>("devices", cancellationToken: token);

                        var bindings = await _client.CallAsync<BridgeBinding[]>("bindings", cancellationToken: token);

                        var authorized = bindings.Any(b => b.DesktopId == _desktopId && b.Approved);

                        _connected = authorized && desktops.Any(d => d.Id == _desktopId && d.SeenAt > _client.UtcNow.AddSeconds(-45));

                        if (!authorized) { _snapshot = null; _renderer = null; _pageContent.Children.Clear(); }

                        _desktopStatus.Text = (_connected ? T("电脑在线 · ", "Desktop online · ") : T("电脑离线 · ", "Desktop offline · ")) + DateTime.Now.ToString("HH:mm:ss");

                    }

                    delay = 2;

                    _pageContent.IsEnabled = _connected && !_uncertain && _pendingId is null && _snapshot is not null && _snapshot.ExpiresAt > _client.UtcNow;

                }

                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (BridgeException exception) when (exception.Code == "mobile_device_revoked")
                {
                    ShowExpiredDevice();
                    break;
                }

                catch (Exception exception)

                {

                    _connected = false; _pageContent.IsEnabled = false;

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

        if (_storageKey.Length > 0) { SecureStorage.Default.Remove(_storageKey); Preferences.Default.Remove(_storageKey + ".pending"); }

        if (_authentication is not null) await _authentication.LogoutAsync();
        _client = null; _snapshot = null; _renderer = null; _pendingId = null; _uncertain = false;

        _status.Text = ""; _desktopStatus.Text = ""; ShowSignIn();

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

            new MobileNode("Button", "save") { Text = "Save to desktop", ActionId = "mock-save", InputIds = ["content", "pinned"] }));

        RenderPreview(page);

    }

    private void RenderPreview(MobilePage page)

    {
        _isSignIn = false;
        _status.IsVisible = true;

        _preview = true; _snapshot = null;

        _status.Text = T("模拟器 · MOCK · 不执行业务", "Simulator · MOCK · No business execution");

        ShowSnapshot(new("preview", "mock", "mock", "mock", DateTimeOffset.UtcNow.AddMinutes(5), page));

        _pageContent.IsEnabled = true;

        // Preview is deliberately isolated from platform transport.

        _body.Children.RemoveAt(_body.Children.Count - 1);

        _body.Children.RemoveAt(_body.Children.Count - 1);

        _body.Children.Add(Button(T("退出模拟器", "Close simulator"), () => { _preview = false; _snapshot = null; ShowSignIn(); return Task.CompletedTask; }));

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
