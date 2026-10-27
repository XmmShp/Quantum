using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private readonly MobileNavigationState _navigation = new();
    private readonly Border _bottomBar = new() { IsVisible = false };
    private readonly Grid _navigationOverlay = new() { IsVisible = false };
    private readonly Border _navigationSheet = new();
    private readonly Grid _navigationItems = new()
    {
        ColumnSpacing = 8, RowSpacing = 12,
        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
    };
    private bool _navigationExpanded;
    private DateTimeOffset _suppressNavClickUntil;
    private bool _navigationDragging;
#if DEBUG
    private readonly Dictionary<string, MobilePage> _previewNavigation = [];
#endif

    private void BuildNavigation(Grid shell)
    {
        _bottomBar.BackgroundColor = Colors.White;
        _bottomBar.Stroke = MobileTheme.Line;
        _bottomBar.StrokeThickness = 1;
        _bottomBar.Padding = new Thickness(16, 5, 16, 8);
        shell.Add(_bottomBar, 0, 2);
        var up = new PanGestureRecognizer();
        double drag = 0;
        up.PanUpdated += async (_, e) =>
        {
            if (_navigationDragging || DateTimeOffset.UtcNow < _suppressNavClickUntil) return;
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
        var heading = new VerticalStackLayout { Spacing = 0, Children = { handle, new Label { Text = T("插件", "Plugins"), FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Ink } } };
        heading.GestureRecognizers.Add(down);
        var sheet = new Grid { RowSpacing = 12, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        sheet.Add(heading);
        sheet.Add(MobileTheme.Caption(T("长按拖动调整顺序，前四个显示在底栏", "Hold and drag to reorder. The first four appear in the bar.")), 0, 1);
        sheet.Add(new ScrollView { Content = _navigationItems }, 0, 2);
        _navigationSheet.Content = sheet;
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
            var button = Button(page.Icon ?? "◇", async () => { if (DateTimeOffset.UtcNow >= _suppressNavClickUntil) await SelectNavigationPageAsync(page); });
            button.AutomationId = "mobile-nav-" + i;
            button.FontSize = 22;
            button.Padding = 0;
            button.HeightRequest = button.MinimumHeightRequest = 44;
            button.CornerRadius = 12;
            button.BorderWidth = 0;
            button.WidthRequest = 48;
            button.HorizontalOptions = LayoutOptions.Center;
            SemanticProperties.SetDescription(button, page.Title);
            ToolTipProperties.SetText(button, page.Title);
            AddReorderGestures(button, page);
            button.BackgroundColor = selected ? Color.FromArgb("#EEE8F7") : Colors.Transparent;
            button.TextColor = selected ? MobileTheme.Accent : MobileTheme.Muted;
            buttons.Add(button, i, 0);
        }
        var grip = new Border
        {
            WidthRequest = 32, HeightRequest = 3, BackgroundColor = MobileTheme.Line, StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 4)
        };
        _bottomBar.Content = new VerticalStackLayout { Spacing = 0, Children = { grip, buttons } };
        _navigationItems.Children.Clear();
        _navigationItems.RowDefinitions.Clear();
        var index = 0;
        foreach (var page in _navigation.Pages)
        {
            var selected = page.PluginId == _navigation.PluginId && page.PageId == _navigation.PageId;
            var button = Button(page.Icon ?? "◇", async () => { if (DateTimeOffset.UtcNow >= _suppressNavClickUntil) await SelectNavigationPageAsync(page); });
            button.FontSize = 24; button.Padding = 0;
            button.WidthRequest = button.HeightRequest = 48;
            button.MinimumWidthRequest = button.MinimumHeightRequest = 48;
            button.HorizontalOptions = LayoutOptions.Center;
            button.CornerRadius = 14; button.BorderWidth = 0;
            AddReorderGestures(button, page);
            button.BackgroundColor = selected ? Color.FromArgb("#EEE8F7") : MobileTheme.Canvas;
            button.TextColor = selected ? MobileTheme.Accent : MobileTheme.Ink;
            SemanticProperties.SetDescription(button, page.Title);
            var label = new Label
            {
                Text = page.Title, FontSize = 12, TextColor = selected ? MobileTheme.Accent : MobileTheme.Muted,
                HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => { if (DateTimeOffset.UtcNow >= _suppressNavClickUntil) await SelectNavigationPageAsync(page); };
            label.GestureRecognizers.Add(tap);
            AddReorderGestures(label, page);
            var tile = new Grid { RowSpacing = 4, Padding = new Thickness(2, 4), MinimumHeightRequest = 80,
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
            tile.Add(button); tile.Add(label, 0, 1);
            if (index % 4 == 0) _navigationItems.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _navigationItems.Add(tile, index % 4, index / 4);
            index++;
        }
        if (_navigation.Pages.Length == 0)
        {
            var empty = MobileTheme.Caption(T("暂无移动插件页面", "No mobile plugin pages yet"));
            _navigationItems.Add(empty); Grid.SetColumnSpan(empty, 4);
        }
        RenderAccountMenu();
    }

    private void AddReorderGestures(View view, BridgePageInfo page)
    {
        var key = MobileNavigationState.Key(page);
        var drag = new DragGestureRecognizer { CanDrag = true };
        drag.DragStarting += (_, e) =>
        {
            e.Data.Properties["quantum.mobile.page"] = key;
            e.Data.Text = page.Title;
            _navigationDragging = true;
            _suppressNavClickUntil = DateTimeOffset.UtcNow.AddSeconds(1);
            view.Opacity = .55;
        };
        drag.DropCompleted += (_, _) => { _navigationDragging = false; view.Opacity = 1; _suppressNavClickUntil = DateTimeOffset.UtcNow.AddMilliseconds(400); };
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) => e.AcceptedOperation = e.Data.Properties.ContainsKey("quantum.mobile.page") ? DataPackageOperation.Copy : DataPackageOperation.None;
        drop.Drop += (_, e) =>
        {
            if (e.Data.Properties.TryGetValue("quantum.mobile.page", out var source) && source is string from)
                ReorderNavigation(from, key);
        };
        view.GestureRecognizers.Add(drag);
        view.GestureRecognizers.Add(drop);
    }

    private void ReorderNavigation(string source, string target)
    {
        if (!_navigation.Pages.Any(p => MobileNavigationState.Key(p) == source) || !_navigation.Pages.Any(p => MobileNavigationState.Key(p) == target)) return;
        _navigationDragging = false;
        _navigation.Move(source, target);
        if (!_preview && _storageKey.Length > 0 && _desktopId is not null)
            Preferences.Default.Set(_storageKey + ".nav-order." + _desktopId, JsonSerializer.Serialize(_navigation.Order));
        RenderNavigation();
    }

    private void RestoreNavigationOrder()
    {
        if (_storageKey.Length == 0 || _desktopId is null) return;
        try
        {
            var order = Preferences.Default.Get(_storageKey + ".nav-order." + _desktopId, "[]");
            _navigation.RestoreOrder(JsonSerializer.Deserialize<string[]>(order) ?? []);
        }
        catch (JsonException) { _navigation.RestoreOrder([]); }
    }

    private async Task RefreshNavigationAsync()
    {
        if (_preview || _client is null || _desktopId is null) return;
        var pages = await _client.CallAsync<BridgePageInfo[]>("pages", new { desktopId = _desktopId, includeIcons = true });
        _navigation.Replace(pages);
        RenderNavigation();
    }

    private async Task OpenNavigationAsync()
    {
        if (_navigationExpanded || !_bottomBar.IsVisible) return;
        await CloseMenuAsync();
        _navigationExpanded = true;
        _navigationSheet.HeightRequest = Math.Min(Math.Max(180, Height * .72), Math.Min(560, Math.Max(220, 120 + Math.Ceiling(_navigation.Pages.Length / 4d) * 92)));
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
        if (_menuExpanded) { _ = CloseMenuAsync(); return true; }
        if (!_navigationExpanded) return base.OnBackButtonPressed();
        _ = CloseNavigationAsync(); return true;
    }
}
