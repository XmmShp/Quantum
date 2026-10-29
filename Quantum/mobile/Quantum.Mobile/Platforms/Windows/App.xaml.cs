namespace Quantum.MobileApp.WinUI;

public partial class App : Microsoft.Maui.MauiWinUIApplication
{
    public App() => InitializeComponent();
    protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
