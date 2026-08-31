using Microsoft.Extensions.Logging;
using NOF.Hosting;
using NOF.Hosting.Maui;
using Quantum.Application.Plugins;
using Quantum.Infrastructure.Plugins;

namespace Quantum;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = NOFMauiAppBuilder.Create();
        var catalog = LoadPluginCatalog();
        if (PluginDiagnosticsEnabled())
        {
            WritePluginDiagnostics(catalog);
        }

        builder
            .AddApplicationPart(typeof(PluginCatalog).Assembly)
            .AddApplicationPart(typeof(PluginCatalogBootstrapper).Assembly);
        foreach (var plugin in catalog.Plugins)
        {
            builder.AddApplicationPart(plugin.EntryAssembly);
        }

        builder.MauiAppBuilder
            .UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton<PluginStaticAssetFileProvider>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddInitializationStep(new PluginLifecycleInitializationStep());

        var nofApp = builder.BuildAsync().GetAwaiter().GetResult();
        return nofApp.MauiApp;
    }

    private static PluginCatalog LoadPluginCatalog()
    {
        var configuredPath = Environment.GetEnvironmentVariable("QUANTUM_MODULES_PATH");
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Modules");
        var applicationDataPath = Path.Combine(FileSystem.AppDataDirectory, "Modules");
        var preferredPath = !string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Directory.Exists(bundledPath)
                ? bundledPath
                : applicationDataPath;

        try
        {
            Directory.CreateDirectory(preferredPath);
            return new PluginCatalogBootstrapper().Bootstrap(preferredPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (string.Equals(preferredPath, applicationDataPath, StringComparison.Ordinal))
            {
                return new PluginCatalog(
                    [],
                    [new PluginLoadFailure(null, $"Plugin directory '{preferredPath}' is not accessible.", exception)]);
            }

            Directory.CreateDirectory(applicationDataPath);
            var fallbackCatalog = new PluginCatalogBootstrapper().Bootstrap(applicationDataPath);
            return new PluginCatalog(
                fallbackCatalog.Plugins,
                fallbackCatalog.Failures.Prepend(new PluginLoadFailure(
                    null,
                    $"Plugin directory '{preferredPath}' is not accessible; using application data instead.",
                    exception)));
        }
    }

    private static void WritePluginDiagnostics(PluginCatalog catalog)
    {
        foreach (var plugin in catalog.Plugins)
        {
            Console.WriteLine($"[Quantum] Loaded plugin {plugin.Manifest.Id} {plugin.Manifest.Version}.");
        }

        foreach (var failure in catalog.Failures)
        {
            Console.Error.WriteLine($"[Quantum] Plugin load failure ({failure.PluginId?.Value ?? "unknown"}): {failure.Reason}");
        }
    }

    internal static bool PluginDiagnosticsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable("QUANTUM_PLUGIN_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal);
}
