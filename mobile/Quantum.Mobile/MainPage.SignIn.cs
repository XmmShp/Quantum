using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private readonly Picker _language = new() { Title = "语言 / Language", WidthRequest = 112, FontSize = 13, TextColor = MobileTheme.Ink, BackgroundColor = Colors.Transparent };
    private Label _signInError = new();
    private Button _signInButton = new();
    private Button _advancedButton = new();
    private ActivityIndicator _signInActivity = new();
    private bool _signingIn;
    private bool _isSignIn = true;
    private bool _accountAccepted;

    private static void ApplyLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Preferences.Default.Set("quantum.mobile.language", culture.Name);
    }

    private View CreateHeader()
    {
        var header = new Grid
        {
            Padding = new Thickness(24, 14),
            ColumnSpacing = 12,
            BackgroundColor = Colors.White,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        header.Add(new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeThickness = 0,
            BackgroundColor = MobileTheme.Accent,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Label { Text = "Q", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        });
        header.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 0,
            Children =
            {
                new Label { Text = "Quantum", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink },
                new Label { Text = "MOBILE", FontSize = 10, CharacterSpacing = 2, TextColor = MobileTheme.Muted }
            }
        }, 1);
        _language.ItemsSource = new[] { "简体中文", "English" };
        _language.SelectedIndex = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? 0 : 1;
        SemanticProperties.SetDescription(_language, "语言 / Language");
        _language.SelectedIndexChanged += async (_, _) =>
        {
            ApplyLanguage(_language.SelectedIndex == 0 ? "zh-CN" : "en-US");
            try
            {
                if (_isSignIn)
                {
                    ShowSignIn();
                }
                else if (_desktopId is null)
                {
                    RenderAccountMenu();
                }
                else if (_snapshot is null)
                {
                    await ShowPagesAsync();
                }
            }
            catch (Exception exception)
            {
                _status.Text = FriendlyError(exception);
            }
        };
        header.Add(_language, 2);
        return header;
    }

    private void ShowSignIn()
    {
        ResetNavigation();
        _isSignIn = true;
        _titleBar.IsVisible = false; _menuOverlay.IsVisible = false; _menuExpanded = false;
        _desktopChoices.IsVisible = false;
        _language.IsEnabled = true;
        _status.IsVisible = false;
        _desktopStatus.IsVisible = false;
        var email = _email.Text ?? Preferences.Default.Get("quantum.mobile.email", "");
        var password = _password.Text;
        _body.Children.Clear();
        _syncPanel.IsVisible = false;
        _body.Children.Add(_signInHeader);
        _body.Children.Add(new Label { Text = T("连接你的 Quantum", "Connect your Quantum"), FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink, Margin = new Thickness(0, 12, 0, 0) });
        _body.Children.Add(new Label { Text = T("让电脑上的插件随身可用。数据和业务仍留在你的电脑上。", "Take your desktop plugins with you. Your data and business operations stay on your computer."), TextColor = MobileTheme.Muted, FontSize = 15, LineHeight = 1.35, Margin = new Thickness(0, 0, 0, 12) });
        _email = new Entry { Text = email, Placeholder = "name@example.com", Keyboard = Keyboard.Email, ReturnType = ReturnType.Next, TextColor = MobileTheme.Ink, PlaceholderColor = MobileTheme.Muted, BackgroundColor = Colors.Transparent, FontSize = 15, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false };
        _password = new Entry { Text = password, Placeholder = T("输入密码", "Enter password"), IsPassword = true, ReturnType = ReturnType.Go, TextColor = MobileTheme.Ink, PlaceholderColor = MobileTheme.Muted, BackgroundColor = Colors.Transparent, FontSize = 15 };
        SemanticProperties.SetDescription(_email, T("平台登录邮箱", "Platform sign-in email"));
        SemanticProperties.SetDescription(_password, T("密码", "Password"));
        _email.Completed += (_, _) => _password.Focus();
        _password.Completed += async (_, _) => await SubmitSignInAsync();
        var passwordField = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        passwordField.Add(_password);
        var reveal = new Button { Text = T("显示", "Show"), FontSize = 12, TextColor = MobileTheme.Accent, BackgroundColor = Colors.Transparent, MinimumWidthRequest = 48, Padding = 6 };
        reveal.Clicked += (_, _) =>
        {
            _password.IsPassword = !_password.IsPassword;
            reveal.Text = _password.IsPassword ? T("显示", "Show") : T("隐藏", "Hide");
        };
        passwordField.Add(reveal, 1);
        _signInError = new Label { FontSize = 13, TextColor = MobileTheme.Error, IsVisible = false, LineHeight = 1.25 };
        _signInActivity = new ActivityIndicator { Color = MobileTheme.Accent, IsVisible = false, HeightRequest = 20 };
        _signInButton = Button(T("登录并连接", "Sign in and connect"), SubmitSignInAsync);
        var form = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = T("登录平台账号", "Sign in to your account"), FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink },
                MobileTheme.Caption(T("使用与开发者后台相同的邮箱和密码。", "Use the same email and password as the developer portal.")),
                new Label { Text = T("邮箱", "Email"), FontSize = 13, TextColor = MobileTheme.Ink, Margin = new Thickness(0, 10, 0, 0) },
                MobileTheme.Input(_email),
                new Label { Text = T("密码", "Password"), FontSize = 13, TextColor = MobileTheme.Ink },
                MobileTheme.Input(passwordField), _signInError, _signInActivity, _signInButton
            }
        };
        _body.Children.Add(MobileTheme.Card(form));
        _advancedButton = Button(T("高级设置  ›", "Advanced settings  ›"), async () =>
        {
            await Navigation.PushAsync(new AdvancedSettingsPage(_baseUrl, value => _baseUrl = value));
        });
        _advancedButton.BackgroundColor = Colors.Transparent;
        _advancedButton.TextColor = MobileTheme.Muted;
        _advancedButton.FontAttributes = FontAttributes.None;
        _body.Children.Add(_advancedButton);
        _body.Children.Add(MobileTheme.Caption(T("登录后自动连接上次使用的电脑。", "After signing in, reconnect automatically to your last desktop.")));
#if DEBUG
        var debug = new VerticalStackLayout { Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        debug.Children.Add(MobileTheme.Caption(T("开发工具 · 仅调试版本", "Developer tools · Debug builds only")));
        var mock = Button(T("打开模拟器 · Mock 模式", "Open simulator · Mock mode"), () => { _isSignIn = false; _status.IsVisible = true; Preview(); return Task.CompletedTask; });
        mock.BackgroundColor = Colors.White;
        mock.TextColor = MobileTheme.Accent;
        mock.BorderColor = MobileTheme.Line;
        mock.BorderWidth = 1;
        debug.Children.Add(mock);
#if WINDOWS || MACCATALYST
        var json = Button(T("模拟器 · 打开 UI JSON", "Simulator · Open UI JSON"), PreviewFileAsync);
        json.BackgroundColor = Colors.White;
        json.TextColor = MobileTheme.Accent;
        json.BorderColor = MobileTheme.Line;
        json.BorderWidth = 1;
        debug.Children.Add(json);
#endif
        _body.Children.Add(debug);
#endif
    }

    private async Task SubmitSignInAsync()
    {
        if (_signingIn || _restoring)
        {
            return;
        }
        _signingIn = true;
        _accountAccepted = false;
        _signInError.IsVisible = false;
        _language.IsEnabled = false;
        _email.IsEnabled = false;
        _password.IsEnabled = false;
        _signInButton.IsEnabled = false;
        _advancedButton.IsEnabled = false;
        _signInActivity.IsVisible = _signInActivity.IsRunning = true;
        _signInButton.Text = T("正在连接…", "Connecting…");
        try
        {
            if (string.IsNullOrWhiteSpace(_email.Text) || !_email.Text.Contains('@') || string.IsNullOrEmpty(_password.Text))
            {
                _signInError.Text = T("请输入开发者后台绑定的邮箱和密码；登录不使用用户名。", "Enter your developer portal email and password. Sign-in uses email, not username.");
                _signInError.IsVisible = true;
                return;
            }
            await SignInAsync();
        }
        catch (Exception exception)
        {
            _signInError.Text = SignInError(exception);
            _signInError.IsVisible = true;
        }
        finally
        {
            _signingIn = false;
            _language.IsEnabled = true;
            _email.IsEnabled = true;
            _password.IsEnabled = true;
            _signInButton.IsEnabled = true;
            _advancedButton.IsEnabled = true;
            _signInButton.Text = T("登录并连接", "Sign in and connect");
            _signInActivity.IsVisible = _signInActivity.IsRunning = false;
        }
    }

    private string SignInError(Exception exception) => _accountAccepted
        ? T("账号已登录，但设备连接未完成：", "Signed in, but the device connection could not finish: ") + FriendlyError(exception)
        : FriendlyError(exception);

    private static string FriendlyError(Exception exception) => exception switch
    {
        PlatformAuthenticationException => T("登录已过期或被撤销，请重新登录。", "Your session has expired or was revoked. Sign in again."),
        PlatformRpcException { Code: "invalid_credentials" } => T("邮箱或密码不正确。", "The email or password is incorrect."),
        BridgeException { Code: "invalid_credentials" } => T("邮箱或密码不正确，请使用开发者后台绑定的邮箱。", "The email or password is incorrect. Use your developer portal email."),
        BridgeException { Code: "invalid_platform_address" } => T("平台地址无效，请在高级设置中填写 HTTPS 地址；仅本机调试允许 HTTP。", "Invalid platform address. Use HTTPS in Advanced settings; HTTP is allowed only for localhost."),
        BridgeException { Code: "login_service_unavailable" } => T("平台登录服务暂不可用，请稍后重试或检查高级设置。", "The sign-in service is unavailable. Retry later or check Advanced settings."),
        HttpRequestException => T("无法连接平台，请检查网络和高级设置中的平台地址。", "Could not reach the platform. Check your connection and the address in Advanced settings."),
        TaskCanceledException => T("连接超时，请重试。", "The connection timed out. Please try again."),
        _ => exception.Message
    };
}
