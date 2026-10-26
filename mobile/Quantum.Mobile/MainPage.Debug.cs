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
                if (_navigationExpanded || _uncertain || _pendingId is not null || !_pageContent.IsEnabled || !_preview && (!_connected || _snapshot.ExpiresAt <= _client!.UtcNow))
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
            _desktopId = id; await ShowPagesAsync();
            return DebugState();
        }
        if (_desktopId is null) throw new MobileDebugException(409, "debug_select_desktop_first");
        if (path == "/pages") return await _client.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId });
        if (path == "/page/open")
        {
            var open = body.Deserialize<DebugOpenRequest>(DebugJson)!;
            var pages = await _client.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId });
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
        connected = _connected, preview = _preview,
        pending = _pendingId is not null, uncertain = _uncertain,
        status = _status.Text, desktopStatus = _desktopStatus.Text,
        desktopId = _desktopId,
        navigation = new
        {
            visible = _bottomBar.IsVisible, expanded = _navigationExpanded,
            pages = _navigation.Pages.Select(p => new { p.PluginId, p.PageId, p.Title, active = p.PluginId == _navigation.PluginId && p.PageId == _navigation.PageId }).ToArray()
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
        _navigation.Replace(_previewNavigation.Values.Select(p => new BridgePageInfo("mock", p.Id, p.Title)));
        _navigation.Select("mock", page.Id);
        RenderNavigation();
    }
}
