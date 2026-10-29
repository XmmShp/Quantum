using Microsoft.Maui.Controls.Shapes;

namespace Quantum.MobileApp;

internal static class MobileTheme
{
    public static Color Canvas => Color.FromArgb("#F8F7FB");
    public static Color Ink => Color.FromArgb("#363249");
    public static Color Muted => Color.FromArgb("#706883");
    public static Color Line => Color.FromArgb("#E2E0E7");
    public static Color Accent => Color.FromArgb("#6D5A96");
    public static Color Error => Color.FromArgb("#AA425B");

    public static Button PrimaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Accent,
        TextColor = Colors.White,
        CornerRadius = 10,
        MinimumHeightRequest = 48,
        FontSize = 15,
        FontAttributes = FontAttributes.Bold,
        Padding = new Thickness(16, 12)
    };

    public static Border Card(View content) => new()
    {
        BackgroundColor = Colors.White,
        Stroke = Line,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 16 },
        Padding = 22,
        Content = content
    };

    public static Border Input(View content) => new()
    {
        BackgroundColor = Colors.White,
        Stroke = Line,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 9 },
        Padding = new Thickness(10, 2),
        Content = content
    };

    public static Label Caption(string text) => new() { Text = text, FontSize = 13, TextColor = Muted, LineHeight = 1.3 };
}
