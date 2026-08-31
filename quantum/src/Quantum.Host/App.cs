namespace Quantum.Host;

public sealed class App(MainPage mainPage) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(mainPage)
        {
            Title = "Quantum",
            MinimumWidth = 960,
            MinimumHeight = 640,
            Width = 1280,
            Height = 820
        };

        return window;
    }
}
