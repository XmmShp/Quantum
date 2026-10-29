using Android.App;
using Android.Runtime;
namespace Quantum.MobileApp;

[Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership) : Microsoft.Maui.MauiApplication(handle, ownership)
{
    protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
