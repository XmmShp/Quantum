namespace Quantum.MobileApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}

public sealed class App : Application
{
    public App()
    {
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage();
        var window = new Window(new NavigationPage(page) { BarBackgroundColor = Colors.White, BarTextColor = MobileTheme.Ink }) { Title = "Quantum Mobile", Width = 430, Height = 860 };
        window.Stopped += (_, _) => page.Suspend();
        window.Resumed += (_, _) => page.Resume();
        window.Destroying += (_, _) => page.Suspend();
#if DEBUG
        page.StartDebugInterface();
        window.Destroying += async (_, _) => await page.StopDebugInterfaceAsync();
#endif
        return window;
    }
}
