using Microsoft.Maui.Controls.Shapes;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private readonly MobileNavigationState _navigation = new();
    private readonly Border _bottomBar = new() { IsVisible = false };
    private readonly Grid _navigationOverlay = new() { IsVisible = false };
    private readonly Border _navigationSheet = new();
    private readonly VerticalStackLayout _navigationItems = new() { Spacing = 8 };
    private bool _navigationExpanded;
#if DEBUG
    private readonly Dictionary<string, MobilePage> _previewNavigation = [];
#endif

    private void BuildNavigation(Grid shell)
    {
        shell.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _bottomBar.BackgroundColor = Colors.White;
        _bottomBar.Stroke = MobileTheme.Line;
        _bottomBar.StrokeThickness = 1;
        _bottomBar.Padding = new Thickness(12, 6, 12, 12);
        shell.Add(_bottomBar, 0, 2);
        var up = new PanGestureRecognizer();
        double drag = 0;
        up.PanUpdated += async (_, e) =>
        {
            if (e.StatusType == GestureStatus.Started) drag = 0;
            if (e.StatusType == GestureStatus.Running) drag = e.TotalY;
            if (e.StatusType == GestureStatus.Completed && drag < -24) await OpenNavigationAsync();
        };
        _bottomBar.GestureRecognizers.Add(up);

        var backdrop = new BoxView { Color = Color.FromArgb("#66000000") };
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += async (_, _) => await CloseNavigationAsync();
        backdrop.GestureRecognizers.Add(dismiss);
        _navigationOverlay.Add(backdrop);
        _navigationSheet.BackgroundColor = Colors.White;
        _navigationSheet.Stroke = MobileTheme.Line;
        _navigationSheet.StrokeThickness = 1;
        _navigationSheet.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24, 24, 0, 0) };
        _navigationSheet.Padding = new Thickness(20, 10, 20, 20);
        _navigationSheet.VerticalOptions = LayoutOptions.End;
        var handle = new Border
        {
            WidthRequest = 38, HeightRequest = 4, HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = MobileTheme.Line, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 2 }, Margin = new Thickness(0, 4, 0, 14)
        };
        var down = new PanGestureRecognizer();
        double sheetDrag = 0;
        down.PanUpdated += async (_, e) =>
        {
            if (e.StatusType == GestureStatus.Started) sheetDrag = 0;
            if (e.StatusType == GestureStatus.Running)
            {
                sheetDrag = Math.Max(0, e.TotalY);
                _navigationSheet.TranslationY = sheetDrag;
            }
            if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
            {
                if (sheetDrag > 65) await CloseNavigationAsync();
                else await _navigationSheet.TranslateToAsync(0, 0, 140, Easing.CubicOut);
            }
        };
        var heading = new VerticalStackLayout { Spacing = 0, Children = { handle, new Label { Text = T("插件", "Plugins"), FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink } } };
        heading.GestureRecognizers.Add(down);
        _navigationSheet.Content = new VerticalStackLayout
        {
            Spacing = 16,
            Children = { heading, new ScrollView { Content = _navigationItems, MaximumHeightRequest = 420 } }
        };
        _navigationOverlay.Add(_navigationSheet);
        shell.Add(_navigationOverlay, 0, 1);
        Grid.SetRowSpan(_navigationOverlay, 2);
        SemanticProperties.SetDescription(_bottomBar, T("插件导航，向上拖动展开全部插件", "Plugin navigation. Pull up to see all plugins."));
    }

    private void RenderNavigation()
    {
        _bottomBar.IsVisible = _desktopId is not null;
#if DEBUG
        _bottomBar.IsVisible |= _preview && _previewNavigation.Count > 0;
#endif
        var shortcuts = _navigation.Shortcuts();
        var buttons = new Grid { ColumnSpacing = 6 };
        for (var i = 0; i < shortcuts.Length; i++)
        {
            buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var page = shortcuts[i];
            var selected = page.PluginId == _navigation.PluginId && page.PageId == _navigation.PageId;
            var button = Button(page.Title, () => SelectNavigationPageAsync(page));
            button.AutomationId = "mobile-nav-" + i;
            button.FontSize = 13; button.Padding = new Thickness(8, 10);
            button.BackgroundColor = selected ? Color.FromArgb("#EEE8F7") : Colors.Transparent;
            button.TextColor = selected ? MobileTheme.Accent : MobileTheme.Muted;
            buttons.Add(button, i, 0);
        }
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var more = Button(T("全部 ⌃", "All ⌃"), OpenNavigationAsync);
        more.AutomationId = "mobile-nav-expand";
        more.BackgroundColor = Colors.Transparent; more.TextColor = MobileTheme.Ink; more.FontSize = 13;
        buttons.Add(more, shortcuts.Length, 0);
        var grip = new Border
        {
            WidthRequest = 32, HeightRequest = 3, BackgroundColor = MobileTheme.Line, StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 4)
        };
        _bottomBar.Content = new VerticalStackLayout { Spacing = 0, Children = { grip, buttons } };
        _navigationItems.Children.Clear();
        foreach (var page in _navigation.Pages)
        {
            var selected = page.PluginId == _navigation.PluginId && page.PageId == _navigation.PageId;
            var button = Button((selected ? "✓  " : "") + page.Title, () => SelectNavigationPageAsync(page));
            button.BackgroundColor = selected ? Color.FromArgb("#EEE8F7") : MobileTheme.Canvas;
            button.TextColor = selected ? MobileTheme.Accent : MobileTheme.Ink;
            _navigationItems.Children.Add(button);
        }
        if (_navigation.Pages.Length == 0) _navigationItems.Children.Add(MobileTheme.Caption(T("暂无移动插件页面", "No mobile plugin pages yet")));
        if (!_preview)
        {
            _navigationItems.Children.Add(Button(T("刷新插件", "Refresh plugins"), RefreshNavigationAsync));
            _navigationItems.Children.Add(Button(T("切换电脑", "Switch desktop"), ShowDevicesAsync));
        }
    }

    private async Task RefreshNavigationAsync()
    {
        if (_preview || _client is null || _desktopId is null) return;
        var pages = await _client.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId });
        _navigation.Replace(pages);
        RenderNavigation();
    }

    private async Task OpenNavigationAsync()
    {
        if (_navigationExpanded || !_bottomBar.IsVisible) return;
        _navigationExpanded = true;
        _navigationSheet.MaximumHeightRequest = Math.Max(220, Math.Min(560, Height * .72));
        _navigationSheet.TranslationY = 80;
        _navigationOverlay.IsVisible = true;
        await _navigationSheet.TranslateToAsync(0, 0, 160, Easing.CubicOut);
        try { await RefreshNavigationAsync(); }
        catch (Exception e) { _status.Text = FriendlyError(e); }
    }

    private async Task CloseNavigationAsync()
    {
        if (!_navigationExpanded) return;
        _navigationExpanded = false;
        await _navigationSheet.TranslateToAsync(0, 80, 120, Easing.CubicIn);
        if (!_navigationExpanded) _navigationOverlay.IsVisible = false;
    }

    private async Task SelectNavigationPageAsync(BridgePageInfo page)
    {
#if DEBUG
        if (_preview && _previewNavigation.TryGetValue(page.PageId, out var mock))
        {
            if (_renderer?.IsDirty == true && !await DisplayAlertAsync(T("切换插件", "Switch plugin"), T("丢弃未提交的输入？", "Discard unsubmitted input?"), T("切换", "Switch"), T("取消", "Cancel"))) return;
            _navigation.Select(page.PluginId, page.PageId);
            await CloseNavigationAsync(); RenderPreview(mock); RenderNavigation(); return;
        }
#endif
        await OpenAsync(page.PluginId, page.PageId);
    }

    private void ResetNavigation()
    {
        _bottomBar.IsVisible = false; _navigationOverlay.IsVisible = false; _navigationExpanded = false;
        _navigation.Clear();
#if DEBUG
        _previewNavigation.Clear();
#endif
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_navigationExpanded) return base.OnBackButtonPressed();
        _ = CloseNavigationAsync(); return true;
    }
}
