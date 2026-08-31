using Microsoft.AspNetCore.Components.WebView.Maui;
using Quantum.Components;
using Quantum.Infrastructure.Plugins;

namespace Quantum;

public sealed class MainPage : ContentPage
{
    public MainPage(PluginStaticAssetFileProvider pluginAssets)
    {
        Title = "Quantum";
        BackgroundColor = Color.FromArgb("#F6F5FB");

        var blazorWebView = new PluginBlazorWebView(pluginAssets)
        {
            HostPage = "wwwroot/index.html"
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Routes)
        });

        Content = blazorWebView;
    }
}
