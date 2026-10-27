namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private ContentPage? _settingsPage;

    private async Task ShowSettingsAsync()
    {
        await CloseNavigationAsync();
        var page = _settingsPage = new ContentPage { Title = T("设置", "Settings"), BackgroundColor = MobileTheme.Canvas };
        var language = new Picker { Title = T("语言", "Language"), ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = Chinese ? 0 : 1 };
        language.SelectedIndexChanged += (_, _) =>
        {
            ApplyLanguage(language.SelectedIndex == 0 ? "zh-CN" : "en-US");
            page.Title = T("设置", "Settings");
            RenderNavigation();
        };
        var stack = new VerticalStackLayout
        {
            Padding = 24, Spacing = 16,
            Children =
            {
                MobileTheme.Caption(_authentication?.User?.Email ?? "Quantum"),
                language,
                Button(T("连接状态", "Connection status"), async () =>
                {
                    await Navigation.PopAsync();
                    _syncPanel.IsVisible = true;
                }),
                Button(T("刷新插件列表", "Refresh plugin list"), RefreshNavigationAsync),
                Button(T("切换电脑", "Switch desktop"), async () =>
                {
                    if (_renderer?.IsDirty == true && !await page.DisplayAlertAsync(T("切换电脑", "Switch desktop"), T("丢弃未提交的输入？", "Discard unsubmitted input?"), T("切换", "Switch"), T("取消", "Cancel"))) return;
                    if (_pendingId is not null || _uncertain)
                    {
                        await page.DisplayAlertAsync(T("操作待确认", "Outcome pending"), T("请先核对电脑上的操作结果。", "Check the operation result on desktop first."), T("确定", "OK"));
                        return;
                    }
                    await Navigation.PopAsync();
                    await ShowDevicesAsync();
                }),
                Button(T("退出登录", "Sign out"), async () =>
                {
                    await Navigation.PopAsync();
                    await SignOutAsync();
                })
            }
        };
        page.Content = new ScrollView { Content = stack };
        await Navigation.PushAsync(page);
    }
}
