using System.Globalization;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed class AdvancedSettingsPage : ContentPage
{
    private static string T(string zh, string en) => Preferences.Default.Get("quantum.mobile.language", CultureInfo.CurrentUICulture.Name).StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;

    public AdvancedSettingsPage(string currentAddress, Action<string> saved)
    {
        Title = T("高级设置", "Advanced settings");
        BackgroundColor = MobileTheme.Canvas;
        var address = new Entry
        {
            Text = currentAddress,
            Keyboard = Keyboard.Url,
            FontSize = 15,
            TextColor = MobileTheme.Ink,
            BackgroundColor = Colors.Transparent,
            Placeholder = PlatformAddress.DefaultUrl,
            IsTextPredictionEnabled = false,
            IsSpellCheckEnabled = false
        };
        SemanticProperties.SetDescription(address, T("平台 Base URL", "Platform base URL"));
        var error = new Label { TextColor = MobileTheme.Error, FontSize = 13, IsVisible = false };
        var save = MobileTheme.PrimaryButton(T("保存设置", "Save settings"));
        save.Clicked += async (_, _) =>
        {
            try
            {
                var uri = PlatformAddress.Parse(address.Text);
                Preferences.Default.Set("quantum.mobile.platform", uri.AbsoluteUri);
                saved(uri.AbsoluteUri);
                await Navigation.PopAsync();
            }
            catch (BridgeException)
            {
                error.Text = T("请输入有效的 HTTPS 平台地址。HTTP 仅允许 localhost。", "Enter a valid HTTPS platform address. HTTP is allowed only for localhost.");
                error.IsVisible = true;
            }
        };
        var reset = new Button { Text = T("恢复官方平台地址", "Use the official platform"), BackgroundColor = Colors.Transparent, TextColor = MobileTheme.Accent, FontSize = 13 };
        reset.Clicked += (_, _) => { address.Text = PlatformAddress.DefaultUrl; error.IsVisible = false; };
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 20,
                MaximumWidthRequest = 480,
                HorizontalOptions = LayoutOptions.Fill,
                Children =
                {
                    new Label { Text = T("平台连接", "Platform connection"), FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink },
                    MobileTheme.Caption(T("通常无需修改。仅在使用自部署平台或本地开发服务时调整。", "You usually do not need to change this. Use it for a self-hosted platform or local development.")),
                    MobileTheme.Card(new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            new Label { Text = "Base URL", TextColor = MobileTheme.Ink, FontSize = 14 },
                            MobileTheme.Input(address),
                            MobileTheme.Caption(T("填写平台根地址，不是开发者后台的 /portal 页面地址。", "Use the platform base address, not the developer portal's /portal page URL.")),
                            error, save, reset
                        }
                    })
                }
            }
        };
    }
}
