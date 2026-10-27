using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private bool _refreshInProgress;
    private Task? _refreshTask;
    private TaskCompletionSource? _refreshCompletion;
    private int _refreshCount;
    private readonly Label _noticeText = new() { FontSize = 13, TextColor = MobileTheme.Error };
    private Button _reviewPending = new();
    private Button _dismissNotice = new();

    private Task RefreshFromPullAsync()
    {
        if (_refreshInProgress) return _refreshTask ?? Task.CompletedTask;
        _refreshInProgress = true;
        _refreshTask = RefreshFromPullCoreAsync();
        return _refreshTask;
    }

    private async Task RefreshFromPullCoreAsync()
    {
        try
        {
            if (_isSignIn || _restoring || _signingIn || _selectingDesktop || _menuExpanded || _navigationExpanded ||
                _settingsPage is not null && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), _settingsPage)) return;
            _viewport.IsRefreshing = true;
            _refreshCount++;
            if (_pendingId is not null || _uncertain)
            {
                ShowNotice(T("请先核对之前的操作结果；刷新不会重新提交业务操作。", "Check the previous outcome first. Refresh never repeats business actions."));
                return;
            }
            _notice.IsVisible = false;
            if (_preview && _snapshot is { } preview)
            {
                await Task.Delay(200);
                ShowSnapshot(preview with { ViewId = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
                return;
            }
            if (_client is null) return;
            await RefreshDesktopChoicesAsync();
            if (_desktopId is null || _currentDesktop is null || !MobileDesktopSelection.IsOnline(_currentDesktop, _client.UtcNow))
            {
                _connected = false;
                _pageContent.IsEnabled = false;
                if (_snapshot is not null)
                {
                    ShowNotice(T("电脑暂时离线，当前页面和输入已保留。", "The desktop is offline. Current content and input were retained."));
                    return;
                }
                var available = MobileDesktopSelection.Choose(_knownDesktops, _desktopId, _client.UtcNow, Preferences.Default.Get(_storageKey + ".desktop-name", ""));
                if (available is not null && MobileDesktopSelection.IsOnline(available, _client.UtcNow))
                {
                    var firstFrame = _refreshCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    await SelectDesktopAsync(available, false);
                    if (_navigation.Pages.Length > 0) await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                else ShowDesktopUnavailable();
                return;
            }
            _connected = true;
            var pluginId = _snapshot?.PluginId ?? _navigation.PluginId;
            var pageId = _snapshot?.Page.Id ?? _navigation.PageId;
            await RefreshNavigationAsync();
            var page = _navigation.Pages.FirstOrDefault(p => p.PluginId == pluginId && p.PageId == pageId);
            if (page is null)
            {
                if (_renderer?.IsDirty == true)
                {
                    _awaitingSnapshot = true;
                    throw new InvalidOperationException(T("原页面已不可用，未提交内容已保留。", "This page is no longer available. Your unsubmitted input was retained."));
                }
                page = _navigation.Pages.FirstOrDefault();
            }
            if (page is null)
            {
                _snapshot = null; _renderer = null;
                _pageContent.IsEnabled = false;
                _body.Children.Clear();
                _body.Children.Add(MobileTheme.Caption(T("这台电脑暂时没有移动插件页面。", "No mobile plugin pages on this desktop yet.")));
                return;
            }
            var completion = _refreshCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_snapshot is null || _snapshot.PluginId != page.PluginId || _snapshot.Page.Id != page.PageId)
                await OpenAsync(page.PluginId, page.PageId);
            else
                await RequestSnapshotAsync(page.PluginId, page.PageId);
            // The native indicator stays open until the fresh desktop frame has actually rendered.
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException)
        {
            // Closing or leaving the current page must release the native refresh deferral.
        }
        catch (Exception exception)
        {
            ShowNotice(exception is TimeoutException
                ? T("刷新超时，已保留当前内容，连接恢复后会继续同步。", "Refresh timed out. Current content was retained; synchronization will resume when connected.")
                : FriendlyError(exception));
        }
        finally
        {
            _refreshCompletion = null;
            _viewport.IsRefreshing = false;
            _refreshInProgress = false;
        }
    }

    private void ShowNotice(string text)
    {
        _noticeText.Text = text;
        _reviewPending.IsVisible = _pendingId is not null || _uncertain;
        _dismissNotice.IsVisible = !_reviewPending.IsVisible;
        _notice.IsVisible = true;
    }
}
