using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Quantum.Mobile.Internal;
using System.Text.Json;

namespace Quantum.MobileApp;

/// <summary>Fixed native component set, shared by the phone application and Windows simulator.</summary>
public sealed class MobileRenderer
{
    private readonly Dictionary<string, JsonElement> _inputs = [];
    private readonly Dictionary<string, View> _views = [];
    private Dictionary<string, MobileNode> _nodes = [];
    private string _contentRootId = "";
    private readonly Func<string, Dictionary<string, JsonElement>, Task> _invoke;
#if DEBUG
    private readonly Dictionary<string, View> _debugControls = [];
    private readonly Dictionary<string, Func<Task>> _debugPress = [];
    public string DebugViewId { get; private set; } = "";
#endif
    public bool IsDirty { get; private set; }
    public MobileRenderer(Func<string, Dictionary<string, JsonElement>, Task> invoke) => _invoke = invoke;
    public View Render(MobilePage page)
    {
        MobileUiValidator.Validate(page, wire: true);
        _contentRootId = page.Root.Type == "Scroll" ? page.Root.Children[0].Id : page.Root.Id;
        _inputs.Clear(); _views.Clear(); _nodes = Flatten(page.Root).ToDictionary(n => n.Id); IsDirty = false;
#if DEBUG
        _debugControls.Clear(); _debugPress.Clear(); DebugViewId = Guid.NewGuid().ToString("N");
#endif
        return Node(page.Root);
    }
    public View Update(MobilePage page)
    {
        MobileUiValidator.Validate(page, wire: true);
        _contentRootId = page.Root.Type == "Scroll" ? page.Root.Children[0].Id : page.Root.Id;
        var previous = _nodes;
        _nodes = Flatten(page.Root).ToDictionary(n => n.Id);
        foreach (var id in _views.Keys.Where(id => !_nodes.ContainsKey(id) || previous[id].Type != _nodes[id].Type).ToArray())
        {
            _views.Remove(id); _inputs.Remove(id);
#if DEBUG
            _debugControls.Remove(id); _debugPress.Remove(id);
#endif
        }
        var root = Node(page.Root);
#if DEBUG
        DebugViewId = Guid.NewGuid().ToString("N");
#endif
        return root;
    }

    private static IEnumerable<MobileNode> Flatten(MobileNode node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));

    private static void Reconcile(Layout layout, View[] desired)
    {
        for (var i = 0; i < desired.Length; i++)
        {
            if (i < layout.Children.Count && ReferenceEquals(layout.Children[i], desired[i])) continue;
            Detach(desired[i]);
            layout.Children.Insert(i, desired[i]);
        }
        while (layout.Children.Count > desired.Length) layout.Children.RemoveAt(layout.Children.Count - 1);
    }

    private static void Detach(View view)
    {
        if (view.Parent is Layout layout) layout.Children.Remove(view);
        else if (view.Parent is ContentView container) container.Content = null;
    }

    private static void SetContent(ContentView container, View content)
    {
        if (ReferenceEquals(container.Content, content)) return;
        Detach(content);
        container.Content = content;
    }

    private static void StyleText(Label label, MobileNode node)
    {
        label.FontSize = node.Style == "title" ? 18 : node.Style == "caption" ? 12 : 14;
        label.FontAttributes = node.Style == "title" ? FontAttributes.Bold : FontAttributes.None;
        label.TextColor = node.Style == "error" ? MobileTheme.Error : node.Style == "caption" ? MobileTheme.Muted : MobileTheme.Ink;
        label.LineHeight = 1.2;
    }

    private void StyleGroup(Border border, MobileNode node)
    {
        var card = node.Id != _contentRootId && node.Children.Any(n => n.Type == "Text" && n.Style == "title" ||
            n.Type == "HStack" && n.Children.FirstOrDefault() is { Type: "Text", Style: "title" });
        ((VerticalStackLayout)border.Content!).Spacing = node.Id == _contentRootId ? 12 : card ? 8 : 4;
        border.Padding = card ? new Thickness(12) : new Thickness(0);
        border.BackgroundColor = card ? Colors.White : Colors.Transparent;
        border.Stroke = MobileTheme.Line;
        border.StrokeThickness = card ? 1 : 0;
    }

    private void LayoutRow(Grid row, MobileNode node)
    {
        var children = node.Children.Select(Node).ToArray();
        Reconcile(row, children);
        row.ColumnDefinitions.Clear();
        for (var i = 0; i < children.Length; i++)
        {
            var flexible = node.Children[i].Type is "VStack" or "Text" or "TextField" or "Toggle";
            row.ColumnDefinitions.Add(new ColumnDefinition(flexible ? GridLength.Star : GridLength.Auto));
            Grid.SetColumn(children[i], i);
            children[i].VerticalOptions = LayoutOptions.Center;
        }
    }

    private View Node(MobileNode node)
    {
        if (_views.TryGetValue(node.Id, out var existing))
        {
            switch (node.Type)
            {
                case "VStack":
                    var group = (Border)existing;
                    Reconcile((Layout)group.Content!, node.Children.Select(Node).ToArray());
                    StyleGroup(group, node);
                    break;
                case "HStack": LayoutRow((Grid)existing, node); break;
                case "Scroll": SetContent((ContentView)existing, Node(node.Children[0])); break;
                case "Text":
                    var label = (Label)existing;
                    label.Text = node.Text;
                    StyleText(label, node);
                    break;
                case "TextField": ((Entry)existing).Placeholder = node.Placeholder ?? node.Text; ((Entry)existing).MaxLength = node.MaxLength; break;
                case "Toggle": ((Label)((Grid)existing).Children[0]).Text = node.Text; break;
                case "Button": ((Button)existing).Text = node.Text; break;
            }
            existing.IsEnabled = node.Enabled && (node.Type != "Button" || node.ActionId is not null);
            return existing;
        }
        View result;
        switch (node.Type)
        {
            case "VStack":
                var vertical = new VerticalStackLayout { Spacing = node.Id == _contentRootId ? 12 : 8 };
                Reconcile(vertical, node.Children.Select(Node).ToArray());
                var group = new Border { Content = vertical, StrokeShape = new RoundRectangle { CornerRadius = 14 } };
                StyleGroup(group, node);
                result = group; break;
            case "HStack":
                var horizontal = new Grid { ColumnSpacing = 10 };
                LayoutRow(horizontal, node);
                result = horizontal; break;
            case "Scroll":
                var container = new ContentView();
                SetContent(container, Node(node.Children[0]));
                result = container; break;
            case "Text":
                var label = new Label { Text = node.Text, LineBreakMode = LineBreakMode.WordWrap };
                StyleText(label, node);
                result = label;
                break;
            case "TextField":
                var input = new Entry { Placeholder = node.Placeholder ?? node.Text, MaxLength = node.MaxLength, FontSize = 14, TextColor = MobileTheme.Ink, PlaceholderColor = MobileTheme.Muted, BackgroundColor = MobileTheme.Canvas, MinimumHeightRequest = 40 };
                _inputs[node.Id] = MobileJson.Element("");
                input.TextChanged += (_, args) => { _inputs[node.Id] = MobileJson.Element(args.NewTextValue ?? ""); IsDirty = true; };
                result = input; break;
            case "Toggle":
                var toggle = new Switch();
                _inputs[node.Id] = MobileJson.Element(false);
                toggle.Toggled += (_, args) => { _inputs[node.Id] = MobileJson.Element(args.Value); IsDirty = true; };
#if DEBUG
                _debugControls[node.Id] = toggle;
#endif
                var toggleRow = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                toggleRow.Add(new Label { Text = node.Text, FontSize = 13, TextColor = MobileTheme.Ink, VerticalOptions = LayoutOptions.Center });
                toggleRow.Add(toggle, 1);
                result = toggleRow;
                break;
            case "Button":
                var button = new Button { Text = node.Text, CornerRadius = 10, MinimumHeightRequest = 44, MinimumWidthRequest = 44, FontSize = 13, Padding = new Thickness(14, 8), BackgroundColor = MobileTheme.Accent, TextColor = Colors.White };
                button.IsEnabled = node.Enabled && node.ActionId is not null;
                async Task PressAsync()
                {
                    button.IsEnabled = false;
                    try { var current = _nodes[node.Id]; await _invoke(current.ActionId!, current.InputIds.ToDictionary(id => id, id => _inputs[id])); }
                    finally { var current = _nodes[node.Id]; button.IsEnabled = current.Enabled && current.ActionId is not null; }
                }
                button.Clicked += async (_, _) => await PressAsync();
#if DEBUG
                _debugPress[node.Id] = PressAsync;
#endif
                result = button; break;
            case "Divider": result = new BoxView { HeightRequest = 1, Color = MobileTheme.Line, Margin = new Thickness(0, 2) }; break;
            case "Spacer": result = new BoxView { HeightRequest = 8, Opacity = 0 }; break;
            case "Progress": result = new ActivityIndicator { IsRunning = true, Color = MobileTheme.Accent, HeightRequest = 24 }; break;
            default: throw new ArgumentException("Unsupported mobile control.");
        }
        if (node.Type != "Button") result.IsEnabled = node.Enabled;
        SemanticProperties.SetDescription(result, node.Text ?? node.Placeholder ?? node.Id);
#if DEBUG
        _debugControls.TryAdd(node.Id, result);
#endif
        _views[node.Id] = result;
        return result;
    }

#if DEBUG
    public object DebugDescribe(MobileNode node) => new
    {
        node.Id, node.Type, node.Text, node.Placeholder, node.MaxLength,
        enabled = _debugControls.TryGetValue(node.Id, out var control) && DebugEnabled(control),
        value = _inputs.TryGetValue(node.Id, out var value) ? (JsonElement?)value : null,
        actionable = _debugPress.ContainsKey(node.Id),
        children = node.Children.Select(DebugDescribe).ToArray()
    };

    public void DebugSetInput(string id, JsonElement value)
    {
        var control = DebugControl(id);
        if (control is Entry entry && value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= entry.MaxLength)
            entry.Text = value.GetString();
        else if (control is Switch toggle && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            toggle.IsToggled = value.GetBoolean();
        else throw new Debugging.MobileDebugException(400, "debug_input_invalid");
    }

    public Task DebugPressAsync(string id)
    {
        _ = DebugControl(id);
        return _debugPress.TryGetValue(id, out var press) ? press() : throw new Debugging.MobileDebugException(400, "debug_not_a_button");
    }

    public void DebugDiscardDraft() => IsDirty = false;

    private View DebugControl(string id)
    {
        if (string.IsNullOrEmpty(id)) throw new Debugging.MobileDebugException(400, "debug_node_invalid");
        if (!_debugControls.TryGetValue(id, out var control)) throw new Debugging.MobileDebugException(404, "debug_node_not_found");
        if (!DebugEnabled(control)) throw new Debugging.MobileDebugException(409, "debug_control_disabled");
        return control;
    }

    private static bool DebugEnabled(VisualElement control)
    {
        for (Element? current = control; current is not null; current = current.Parent)
            if (current is VisualElement visual && (!visual.IsEnabled || !visual.IsVisible)) return false;
        return true;
    }
#endif
}
