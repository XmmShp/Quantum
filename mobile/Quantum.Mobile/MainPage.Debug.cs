using System.Text.Json;
using System.Text.Json.Serialization;
using Quantum.Mobile.Internal;
using Quantum.MobileApp.Debugging;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private MobileDebugServer? _debugServer;
    private bool _debugPreviewStartup;
    private static readonly JsonSerializerOptions DebugJson = new(JsonSerializerDefaults.Web)
    { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal void StartDebugInterface()
    {
        _debugPreviewStartup = Environment.GetEnvironmentVariable("QUANTUM_MOBILE_DEBUG_PREVIEW") == "1";
        try
        {
            var options = MobileDebugOptions.FromEnvironment();
            var file = Path.Combine(FileSystem.AppDataDirectory, "quantum-mobile-debug.json");
            if (options is null && File.Exists(file))
            {
                if (new FileInfo(file).Length > 4096) throw new ArgumentException("Debug configuration is too large.");
                options = JsonSerializer.Deserialize<MobileDebugOptions>(File.ReadAllText(file), DebugJson);
            }
            if (options is null) return;
            _debugServer = new(options, new DebugTarget(this));
            System.Diagnostics.Debug.WriteLine($"Quantum Mobile debug interface: http://127.0.0.1:{_debugServer.Port}");
        }
        catch (Exception)
        {
            // Never log the configuration or access token.
            System.Diagnostics.Debug.WriteLine("Quantum Mobile debug interface could not start. Check its configuration and port.");
        }
    }

    internal async Task StopDebugInterfaceAsync()
    {
        if (_debugServer is { } server) { _debugServer = null; await server.DisposeAsync(); }
    }

    private sealed class DebugTarget : IMobileDebugTarget
    {
        private readonly MainPage _page;
        public DebugTarget(MainPage page) => _page = page;
        public Task<object> ExecuteAsync(string path, JsonElement body)
            => MainThread.InvokeOnMainThreadAsync(() => _page.ExecuteDebugAsync(path, body));
    }

    private async Task<object> ExecuteDebugAsync(string path, JsonElement body)
    {
        if (path == "/state") return DebugState();
        if (path == "/screenshot")
        {
            if (_isSignIn) throw new MobileDebugException(409, "debug_sign_in_page");
#if WINDOWS
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync((Microsoft.UI.Xaml.UIElement)(Handler?.PlatformView ?? throw new MobileDebugException(409, "debug_window_unavailable")));
            using var pixelsReader = Windows.Storage.Streams.DataReader.FromBuffer(await bitmap.GetPixelsAsync());
            var pixels = new byte[pixelsReader.UnconsumedBufferLength];
            pixelsReader.ReadBytes(pixels);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var png = new byte[(int)stream.Size];
            reader.ReadBytes(png);
            return new { image = Convert.ToBase64String(png), width = bitmap.PixelWidth, height = bitmap.PixelHeight };
#else
            throw new MobileDebugException(501, "debug_screenshot_unavailable");
#endif
        }
        if (path == "/navigation/reorder")
        {
            if (!_bottomBar.IsVisible) throw new MobileDebugException(409, "debug_navigation_unavailable");
            var from = body.GetProperty("source").GetString()!;
            var to = body.GetProperty("target").GetString()!;
            if (!_navigation.Pages.Any(p => MobileNavigationState.Key(p) == from) || !_navigation.Pages.Any(p => MobileNavigationState.Key(p) == to))
                throw new MobileDebugException(404, "debug_page_not_shared");
            ReorderNavigation(from, to);
            return DebugState();
        }
        if (path == "/page/pull-refresh")
        {
            if (!_titleBar.IsVisible) throw new MobileDebugException(401, "debug_sign_in_required");
            if (_menuExpanded || _navigationExpanded || _settingsPage is not null && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), _settingsPage))
                throw new MobileDebugException(409, "debug_action_unavailable");
            await RefreshFromPullAsync();
            return DebugState();
        }
        if (path is "/menu/open" or "/menu/close")
        {
            if (!_titleBar.IsVisible) throw new MobileDebugException(401, "debug_sign_in_required");
            if (path == "/menu/open") await OpenMenuAsync();
            else await CloseMenuAsync();
            return DebugState();
        }
        if (path == "/settings/open")
        {
            if (!_titleBar.IsVisible) throw new MobileDebugException(401, "debug_sign_in_required");
            await ShowSettingsAsync(); return DebugState();
        }
        if (path == "/settings/close")
        {
            if (_settingsPage is null || !ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), _settingsPage)) throw new MobileDebugException(409, "debug_settings_unavailable");
            await Navigation.PopAsync(); return DebugState();
        }
        if (path == "/preview/outcome")
        {
            if (!_preview || _pendingId is null) throw new MobileDebugException(409, "debug_not_pending_preview");
            var status = body.GetProperty("status").GetString();
            if (status is not ("succeeded" or "failed" or "unknown")) throw new MobileDebugException(400, "debug_invalid_status");
            Outcome(new(_pendingId, status, "Mock outcome"));
            return DebugState();
        }
        if (path == "/preview/update")
        {
            if (!_preview || _snapshot is null) throw new MobileDebugException(409, "debug_not_previewing");
            var page = _snapshot.Page with { Root = _snapshot.Page.Root with
            {
                Children = _snapshot.Page.Root.Children.Select(node => node.Id == "title" ? node with { Text = "Live update received" } : node).ToArray()
            } };
            ShowSnapshot(new(Guid.NewGuid().ToString("N"), "mock", _snapshot.PluginId, "mock", DateTimeOffset.UtcNow.AddMinutes(5), page));
            return DebugState();
        }
        if (_restoring || _signingIn) throw new MobileDebugException(409, "debug_app_busy");
        if (path is "/navigation/open" or "/navigation/close" or "/navigation/select")
        {
            if (!_bottomBar.IsVisible) throw new MobileDebugException(409, "debug_navigation_unavailable");
            if (path == "/navigation/open") await OpenNavigationAsync();
            else if (path == "/navigation/close") await CloseNavigationAsync();
            else
            {
                var select = body.Deserialize<DebugOpenRequest>(DebugJson)!;
                var page = _navigation.Pages.FirstOrDefault(p => p.PluginId == select.PluginId && p.PageId == select.PageId)
                    ?? throw new MobileDebugException(404, "debug_page_not_shared");
                CheckDebugDraft(select.DiscardDraft);
                await SelectNavigationPageAsync(page);
            }
            return DebugState();
        }
        if (path == "/preview")
        {
            if (_pendingId is not null || _uncertain || _renderer?.IsDirty == true) throw new MobileDebugException(409, "debug_pending_or_dirty");
            Suspend();
            if (_pollTask is not null) await _pollTask;
            _openRequestId = null; _desktopId = null;
            Preview();
            return DebugState();
        }
        if (path == "/preview/close")
        {
            if (!_preview) throw new MobileDebugException(409, "debug_not_previewing");
            _preview = false; _snapshot = null; _renderer = null; _client = null; _desktopId = null; _openRequestId = null;
            _pageContent.Children.Clear(); ShowSignIn();
            await RestoreLoginAsync(true);
            return DebugState();
        }
        if (path is "/input" or "/press" or "/page/refresh")
        {
            var request = body.Deserialize<DebugViewRequest>(new JsonSerializerOptions(DebugJson) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip })!;
            if (_snapshot is null || _renderer is null || request.ViewId != _renderer.DebugViewId)
                throw new MobileDebugException(409, "debug_stale_view");
            if (path == "/page/refresh")
            {
                _ = body.Deserialize<DebugViewRequest>(DebugJson);
                if (_preview) throw new MobileDebugException(409, "debug_preview_refresh_unavailable");
                CheckDebugDraft(request.DiscardDraft);
                await OpenAsync(_snapshot.PluginId, _snapshot.Page.Id);
            }
            else
            {
                if (_menuExpanded || _settingsPage is not null && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), _settingsPage) || _navigationExpanded || path == "/press" && (_uncertain || _pendingId is not null) || !_pageContent.IsEnabled || !_preview && _snapshot.ExpiresAt <= _client!.UtcNow)
                    throw new MobileDebugException(409, "debug_action_unavailable");
                if (path == "/input")
                {
                    var input = body.Deserialize<DebugInputRequest>(DebugJson)!;
                    _renderer.DebugSetInput(input.NodeId, input.Value);
                }
                else await _renderer.DebugPressAsync(body.Deserialize<DebugPressRequest>(DebugJson)!.NodeId);
            }
            return DebugState();
        }
        if (_authentication?.IsAuthenticated != true || _client is null || _preview) throw new MobileDebugException(401, "debug_sign_in_required");
        if (path == "/devices")
        {
            var devices = await _client.CallAsync<BridgeDevice[]>("devices");
            return devices.Where(d => d.Kind == "desktop" && d.SeenAt > _client.UtcNow.AddSeconds(-45))
                .Select(d => new { d.Id, d.Name }).ToArray();
        }
        if (path == "/desktop/select")
        {
            var id = body.Deserialize<DebugDesktopRequest>(DebugJson)!.DesktopId;
            var devices = await _client.CallAsync<BridgeDevice[]>("devices");
            if (!devices.Any(d => d.Id == id && d.Kind == "desktop")) throw new MobileDebugException(404, "debug_desktop_not_found");
            CheckDebugDraft(false);
            await SelectDesktopAsync(devices.Single(d => d.Id == id), false);
            return DebugState();
        }
        if (_desktopId is null) throw new MobileDebugException(409, "debug_select_desktop_first");
        if (path == "/pages") return await _client.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId, includeIcons = true });
        if (path == "/page/open")
        {
            var open = body.Deserialize<DebugOpenRequest>(DebugJson)!;
            var pages = await _client.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId, includeIcons = true });
            if (!pages.Any(p => p.PluginId == open.PluginId && p.PageId == open.PageId)) throw new MobileDebugException(404, "debug_page_not_shared");
            CheckDebugDraft(open.DiscardDraft);
            await OpenAsync(open.PluginId, open.PageId);
            return DebugState();
        }
        throw new MobileDebugException(404, "debug_route_not_found");
    }

    private void CheckDebugDraft(bool discard)
    {
        if (_renderer?.IsDirty != true) return;
        if (!discard) throw new MobileDebugException(409, "debug_draft_conflict");
        _renderer.DebugDiscardDraft();
    }

    private object DebugState() => new
    {
        authenticated = _authentication?.IsAuthenticated == true,
        connected = _connected, preview = _preview, persistentHeader = !_isSignIn && _signInHeader.Parent is not null, syncPanelVisible = _notice.IsVisible,
        refresh = new { enabled = _viewport.IsRefreshEnabled, active = _viewport.IsRefreshing, count = _refreshCount },
        settingsVisible = _settingsPage is not null && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), _settingsPage),
        titleBar = new { visible = _titleBar.IsVisible, height = _titleBar.HeightRequest, avatar = _avatarInitial.AutomationId, title = _pageTitle.Text },
        menu = new { expanded = _menuExpanded, visible = _menuOverlay.IsVisible, settingsPinnedToBottom = Grid.GetRow(_menuSettings) == 1, desktopId = _desktopId },
        pending = _pendingId is not null, uncertain = _uncertain,
        actionFeedback = new { visible = _actionFeedback.IsVisible, text = _actionFeedback.Text },
        status = _status.Text, desktopStatus = _desktopStatus.Text,
        desktopId = _desktopId,
        navigation = new
        {
            visible = _bottomBar.IsVisible, expanded = _navigationExpanded,
            gridColumns = _navigationItems.ColumnDefinitions.Count,
            tiles = _navigationItems.Children.OfType<Grid>().Select(tile => new { row = Grid.GetRow(tile), column = Grid.GetColumn(tile) }).ToArray(),
            iconButtons = (_bottomBar.Content as VerticalStackLayout)?.Children.OfType<Grid>().LastOrDefault()?.Children.OfType<Button>().Select(b => b.Text).ToArray(),
            shortcuts = _navigation.Shortcuts().Select(p => new { p.PluginId, p.PageId, p.Icon }).ToArray(),
            pages = _navigation.Pages.Select(p => new { p.PluginId, p.PageId, p.Title, p.Icon, active = p.PluginId == _navigation.PluginId && p.PageId == _navigation.PageId }).ToArray()
        },
        page = _snapshot is { } snapshot && _renderer is { } renderer ? new
        {
            viewId = renderer.DebugViewId, snapshot.PluginId, pageId = snapshot.Page.Id,
            snapshot.Page.Title, snapshot.ExpiresAt, dirty = renderer.IsDirty,
            tree = renderer.DebugDescribe(snapshot.Page.Root)
        } : null
    };

    private void SetupPreviewNavigation(MobilePage page)
    {
        _previewNavigation.Clear();
        _previewNavigation[page.Id] = page;
        var status = new MobilePage("mock-status", T("状态预览", "Status preview"),
            new MobileNode("VStack", "root") { Children = [new("Text", "title") { Text = T("连接与导航预览", "Connection and navigation preview") }, new("Text", "mock-info") { Text = "MOCK · No platform or business calls" }] });
        _previewNavigation[status.Id] = status;
        _navigation.Replace(_previewNavigation.Values.Select(p => new BridgePageInfo(p.Id == "mock-status" ? "mock.status" : "mock", p.Id, p.Title) { Icon = p.Id == "mock-status" ? "✦" : "日" }));
        _navigation.Select("mock", page.Id);
        RenderNavigation();
    }
}
