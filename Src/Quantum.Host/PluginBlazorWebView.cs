using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;
using Quantum.Infrastructure.Plugins;

namespace Quantum.Host;

public sealed class PluginBlazorWebView(PluginStaticAssetFileProvider pluginAssets) : BlazorWebView
{
    public override IFileProvider CreateFileProvider(string contentRootDir)
        => new CompositeFileProvider(base.CreateFileProvider(contentRootDir), pluginAssets);
}
