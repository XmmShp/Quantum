using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private async Task EnterDesktopAsync()
    {
        _isSignIn = false;
        _awaitingSnapshot = false;
        _openRequestId = null;
        _status.IsVisible = true;
        _desktopStatus.IsVisible = true;
        _syncPanel.IsVisible = false;
        await RefreshDesktopChoicesAsync();
        var remembered = Preferences.Default.Get(_storageKey + ".desktop", (string?)null);
        var selected = MobileDesktopSelection.Choose(_knownDesktops, remembered, _client!.UtcNow, Preferences.Default.Get(_storageKey + ".desktop-name", ""));
        if (selected is not null && MobileDesktopSelection.IsOnline(selected, _client.UtcNow))
        {
            await SelectDesktopAsync(selected, false);
            return;
        }
        _desktopId = remembered;
        _currentDesktop = selected;
        ShowDesktopUnavailable();
        RenderAccountMenu();
    }

    private void ShowDesktopUnavailable()
    {
        _body.Children.Clear();
        _body.Children.Add(new Label
        {
            Text = _desktopId is null ? T("等待电脑上线", "Waiting for a desktop") : T("上次使用的电脑暂时离线", "Your last desktop is offline"),
            FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink
        });
        _body.Children.Add(MobileTheme.Caption(T("电脑上线后会自动连接，也可以从左上角菜单切换电脑。", "It will connect when the desktop comes online. You can also switch desktops from the top-left menu.")));
    }

    private async Task SelectDesktopAsync(BridgeDevice device, bool confirm)
    {
        if (_selectingDesktop) return;
        if (device.Id == _desktopId && _snapshot is not null) { await CloseMenuAsync(); return; }
        if (_pendingId is not null || _uncertain)
        {
            if (confirm) await DisplayAlertAsync(T("操作待确认", "Outcome pending"), T("请先核对电脑上的操作结果，再切换电脑。", "Check the operation result on desktop before switching computers."), T("确定", "OK"));
            else
            {
                _desktopId = device.Id; _currentDesktop = device;
                _body.Children.Clear();
                _body.Children.Add(new Label { Text = T("上次操作的结果待确认", "Previous outcome needs checking"), FontSize = 24, TextColor = MobileTheme.Ink });
                _body.Children.Add(MobileTheme.Caption(T("请在电脑核对操作结果，再从下拉状态区解除待确认状态。", "Check the outcome on desktop, then clear the pending state in the pull-down status panel.")));
                RenderAccountMenu(); _syncPanel.IsVisible = true;
            }
            return;
        }
        if (confirm && _renderer?.IsDirty == true && !await DisplayAlertAsync(T("切换电脑", "Switch desktop"), T("丢弃未提交的输入？", "Discard unsubmitted input?"), T("切换", "Switch"), T("取消", "Cancel"))) return;
        _selectingDesktop = true;
        try
        {
            await CloseMenuAsync();
            ResetNavigation();
            _pageContent.IsEnabled = false;
            _awaitingSnapshot = false;
            _body.Children.Clear();
            _body.Children.Add(new ActivityIndicator { IsRunning = true, Color = MobileTheme.Accent, HeightRequest = 32 });
            _pageCache.Clear();
            _snapshot = null; _renderer = null; _deferredSnapshot = null; _openRequestId = null;
            _desktopId = device.Id;
            _currentDesktop = device;
            _connected = MobileDesktopSelection.IsOnline(device, _client!.UtcNow);
            Preferences.Default.Set(_storageKey + ".desktop", device.Id);
            Preferences.Default.Set(_storageKey + ".desktop-name", device.Name);
            _desktopChoices.IsVisible = false;
            RenderAccountMenu();
            await ShowPagesAsync();
        }
        finally { _selectingDesktop = false; }
    }
}
