using Foundation;
namespace Quantum.MobileApp;
[Register("AppDelegate")]
public sealed class AppDelegate : Microsoft.Maui.MauiUIApplicationDelegate
{
    protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
