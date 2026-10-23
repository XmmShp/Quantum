using Microsoft.Maui.Graphics;
using Quantum.Mobile.Internal;
using System.Text.Json;

namespace Quantum.MobileApp;

/// <summary>Fixed native component set, shared by the phone application and Windows simulator.</summary>
public sealed class MobileRenderer
{
    private readonly Dictionary<string, JsonElement> _inputs = [];
    private readonly Func<string, Dictionary<string, JsonElement>, Task> _invoke;
    public bool IsDirty { get; private set; }
    public MobileRenderer(Func<string, Dictionary<string, JsonElement>, Task> invoke) => _invoke = invoke;
    public View Render(MobilePage page)
    {
        MobileUiValidator.Validate(page, wire: true);
        _inputs.Clear(); IsDirty = false;
        return Node(page.Root);
    }
    private View Node(MobileNode node)
    {
        View result;
        switch (node.Type)
        {
            case "VStack":
                var vertical = new VerticalStackLayout { Spacing = 14 };
                foreach (var child in node.Children) vertical.Children.Add(Node(child));
                result = vertical; break;
            case "HStack":
                var horizontal = new HorizontalStackLayout { Spacing = 12 };
                foreach (var child in node.Children) horizontal.Children.Add(Node(child));
                result = horizontal; break;
            case "Scroll": result = new ScrollView { Content = Node(node.Children[0]) }; break;
            case "Text":
                result = new Label
                {
                    Text = node.Text,
                    FontSize = node.Style == "title" ? 26 : node.Style == "caption" ? 13 : 17,
                    TextColor = node.Style == "error" ? Colors.IndianRed : null,
                    LineBreakMode = LineBreakMode.WordWrap
                };
                break;
            case "TextField":
                var input = new Entry { Placeholder = node.Placeholder ?? node.Text, MaxLength = node.MaxLength };
                _inputs[node.Id] = MobileJson.Element("");
                input.TextChanged += (_, args) => { _inputs[node.Id] = MobileJson.Element(args.NewTextValue ?? ""); IsDirty = true; };
                result = input; break;
            case "Toggle":
                var toggle = new Switch();
                _inputs[node.Id] = MobileJson.Element(false);
                toggle.Toggled += (_, args) => { _inputs[node.Id] = MobileJson.Element(args.Value); IsDirty = true; };
                result = new HorizontalStackLayout { Spacing = 12, Children = { new Label { Text = node.Text, VerticalOptions = LayoutOptions.Center }, toggle } };
                break;
            case "Button":
                var button = new Button { Text = node.Text, CornerRadius = 12, MinimumHeightRequest = 48 };
                button.IsEnabled = node.Enabled && node.ActionId is not null;
                button.Clicked += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try { await _invoke(node.ActionId!, node.InputIds.ToDictionary(id => id, id => _inputs[id])); }
                    finally { button.IsEnabled = node.Enabled && node.ActionId is not null; }
                };
                result = button; break;
            case "Divider": result = new BoxView { HeightRequest = 1, Color = Colors.Gray, Opacity = .25 }; break;
            case "Spacer": result = new BoxView { HeightRequest = 16, Opacity = 0 }; break;
            case "Progress": result = new ActivityIndicator { IsRunning = true }; break;
            default: throw new ArgumentException("Unsupported mobile control.");
        }
        if (node.Type != "Button") result.IsEnabled = node.Enabled;
        SemanticProperties.SetDescription(result, node.Text ?? node.Placeholder ?? node.Id);
        return result;
    }
}
