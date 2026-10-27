using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private readonly Grid _titleBar = new() { IsVisible = false, HeightRequest = 44 };
    private readonly Label _pageTitle = new() { FontSize = 14, TextColor = MobileTheme.Ink, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
    private readonly Grid _menuOverlay = new() { IsVisible = false, ZIndex = 20 };
    private readonly Border _menuPanel = new();
    private readonly VerticalStackLayout _menuContent = new() { Spacing = 18 };
    private readonly VerticalStackLayout _desktopChoices = new() { Spacing = 6, IsVisible = false };
    private readonly Label _currentDesktopName = new() { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
    private readonly Label _currentDesktopState = new() { FontSize = 12, TextColor = MobileTheme.Muted };
    private Button _avatarInitial = new();
    private Button _menuSettings = new();
    private BridgeDevice[] _knownDesktops = [];
    private BridgeDevice? _currentDesktop;
    private bool _menuExpanded;
    private bool _selectingDesktop;

    private void BuildAccountMenu(Grid shell)
    {
        _titleBar.Padding = new Thickness(12, 4);
        _titleBar.BackgroundColor = Colors.White;
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _titleBar.ColumnSpacing = 12;
        _avatarInitial = Button("Q", OpenMenuAsync);
        _avatarInitial.FontSize = 13;
        _avatarInitial.WidthRequest = _avatarInitial.HeightRequest = 32;
        _avatarInitial.MinimumHeightRequest = _avatarInitial.MinimumWidthRequest = 32;
        _avatarInitial.CornerRadius = 16;
        _avatarInitial.BorderWidth = 0;
        _avatarInitial.Padding = 0;
        _avatarInitial.BackgroundColor = Color.FromArgb("#EEE8F7");
        _avatarInitial.TextColor = MobileTheme.Accent;
        _avatarInitial.AutomationId = "mobile-account-avatar";
        SemanticProperties.SetDescription(_avatarInitial, T("打开账户菜单", "Open account menu"));
        _titleBar.Add(_avatarInitial);
        _titleBar.Add(_pageTitle, 1);
        shell.Add(_titleBar);

        var backdrop = new BoxView { Color = Color.FromArgb("#55000000") };
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += async (_, _) => await CloseMenuAsync();
        backdrop.GestureRecognizers.Add(dismiss);
        _menuOverlay.Add(backdrop);
        _menuPanel.HorizontalOptions = LayoutOptions.Start;
        _menuPanel.BackgroundColor = Colors.White;
        _menuPanel.StrokeThickness = 0;
        _menuPanel.Padding = new Thickness(22, 28, 22, 16);
        var panel = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        panel.Add(new ScrollView { Content = _menuContent });
        _menuSettings = MenuButton(T("设置", "Settings"), ShowSettingsAsync);
        _menuSettings.AutomationId = "mobile-menu-settings";
        _menuSettings.Margin = new Thickness(0, 16, 0, 0);
        panel.Add(_menuSettings, 0, 1);
        _menuPanel.Content = panel;
        _menuOverlay.Add(_menuPanel);
        shell.Add(_menuOverlay);
        Grid.SetRowSpan(_menuOverlay, 3);
        var swipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        swipe.Swiped += async (_, _) => await CloseMenuAsync();
        _menuPanel.GestureRecognizers.Add(swipe);
    }

    private Button MenuButton(string text, Func<Task> action)
    {
        var button = Button(text, action);
        button.BackgroundColor = Colors.Transparent;
        button.TextColor = MobileTheme.Ink;
        button.FontSize = 14;
        button.FontAttributes = FontAttributes.None;
        button.HorizontalOptions = LayoutOptions.Fill;
        button.LineBreakMode = LineBreakMode.TailTruncation;
        button.Padding = new Thickness(10, 12);
        return button;
    }

    private void RenderAccountMenu()
    {
        _titleBar.IsVisible = !_isSignIn && (_authentication?.IsAuthenticated == true || _preview);
        _viewport.IsRefreshEnabled = _titleBar.IsVisible;
        var user = _authentication?.User;
        var name = _preview ? "Quantum Preview" : !string.IsNullOrWhiteSpace(user?.Username) ? user.Username : user?.Email ?? "Quantum";
        _avatarInitial.Text = StringInfo.GetNextTextElement(name).ToUpperInvariant();
        _menuSettings.Text = T("设置", "Settings");
        _pageTitle.Text = _navigation.Current?.Title ?? _currentDesktop?.Name ?? T("Quantum 移动端", "Quantum Mobile");
        _menuContent.Children.Clear();
        _menuContent.Children.Add(new Label { Text = name, FontSize = 21, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation });
        _menuContent.Children.Add(MobileTheme.Caption(_preview ? "preview@example.test" : user?.Email ?? ""));
        _menuContent.Children.Add(new BoxView { HeightRequest = 1, Color = MobileTheme.Line, Margin = new Thickness(0, 6) });
        _menuContent.Children.Add(MobileTheme.Caption(T("当前电脑", "Current desktop")));
        _currentDesktopName.Text = _currentDesktop?.Name ?? (_desktopId is not null ? Preferences.Default.Get(_storageKey + ".desktop-name", T("上次使用的电脑", "Last desktop")) : T("暂无电脑", "No desktop"));
        _currentDesktopState.Text = _connected ? T("在线", "Online") : T("未连接", "Not connected");
        _menuContent.Children.Add(_currentDesktopName);
        _menuContent.Children.Add(_currentDesktopState);
        _menuContent.Children.Add(MenuButton(T("切换电脑  ›", "Switch desktop  ›"), async () =>
        {
            _desktopChoices.IsVisible = !_desktopChoices.IsVisible;
            if (!_preview && _desktopChoices.IsVisible) await RefreshDesktopChoicesAsync();
        }));
        _menuContent.Children.Add(_desktopChoices);
        RenderDesktopChoices();
    }

    private void RenderDesktopChoices()
    {
        _desktopChoices.Children.Clear();
        var onlineDesktops = _knownDesktops.Where(d => MobileDesktopSelection.IsOnline(d, _client?.UtcNow ?? DateTimeOffset.UtcNow)).ToArray();
        foreach (var device in onlineDesktops.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            var online = MobileDesktopSelection.IsOnline(device, _client?.UtcNow ?? DateTimeOffset.UtcNow);
            var button = MenuButton((device.Id == _desktopId ? "✓  " : "") + device.Name + (onlineDesktops.Count(d => d.Name == device.Name) > 1 ? " · " + device.Id[..6] : "") + (online ? "" : T(" · 离线", " · Offline")), () => SelectDesktopAsync(device, true));
            button.IsEnabled = online;
            button.AutomationId = "mobile-desktop-" + device.Id;
            _desktopChoices.Children.Add(button);
        }
        if (onlineDesktops.Length == 0) _desktopChoices.Children.Add(MobileTheme.Caption(T("暂无可用电脑", "No desktops available")));
        if (!_preview) _desktopChoices.Children.Add(MenuButton(T("刷新设备", "Refresh devices"), RefreshDesktopChoicesAsync));
    }

    private async Task RefreshDesktopChoicesAsync()
    {
        _knownDesktops = (await _client!.CallAsync<BridgeDevice[]>("devices")).Where(d => d.Kind == "desktop").ToArray();
        _currentDesktop = _knownDesktops.FirstOrDefault(d => d.Id == _desktopId);
        RenderDesktopChoices();
    }

    private async Task OpenMenuAsync()
    {
        if (_menuExpanded || !_titleBar.IsVisible) return;
        await CloseNavigationAsync();
        RenderAccountMenu();
        _menuExpanded = true;
        _menuPanel.WidthRequest = Math.Min(320, Math.Max(240, Width * .84));
        _menuPanel.TranslationX = -_menuPanel.WidthRequest;
        _menuOverlay.IsVisible = true;
        await _menuPanel.TranslateToAsync(0, 0, 170, Easing.CubicOut);
    }

    private async Task CloseMenuAsync()
    {
        if (!_menuExpanded) return;
        _menuExpanded = false;
        await _menuPanel.TranslateToAsync(-_menuPanel.WidthRequest, 0, 130, Easing.CubicIn);
        if (!_menuExpanded) _menuOverlay.IsVisible = false;
    }
}
