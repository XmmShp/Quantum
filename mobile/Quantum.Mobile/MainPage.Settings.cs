namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private ContentPage? _settingsPage;

    private async Task ShowSettingsAsync()
    {
        await CloseNavigationAsync();
        await CloseMenuAsync();
        var page = _settingsPage = new ContentPage { Title = T("设置", "Settings"), BackgroundColor = MobileTheme.Canvas };
        var language = new Picker { Title = T("语言", "Language"), ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = Chinese ? 0 : 1 };
        language.SelectedIndexChanged += (_, _) =>
        {
            ApplyLanguage(language.SelectedIndex == 0 ? "zh-CN" : "en-US");
            page.Title = T("设置", "Settings");
            RenderNavigation();
            RenderAccountMenu();
        };
        var stack = new VerticalStackLayout
        {
            Padding = 24, Spacing = 16,
            Children =
            {
                language,
                Button(T("连接状态", "Connection status"), async () =>
                {
                    await Navigation.PopAsync();
                    _syncPanel.IsVisible = true;
                }),
                Button(T("刷新插件列表", "Refresh plugin list"), RefreshNavigationAsync),
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
