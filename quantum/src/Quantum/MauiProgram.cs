using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;
using NOF.Hosting;
using NOF.Hosting.Maui;
using Quantum.Application.Plugins;
using Quantum.Infrastructure.Plugins;
using Quantum.Plugin.Abstraction;

namespace Quantum;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = NOFMauiAppBuilder.Create();
        var (runtimeOptions, locationFailures) = ResolvePluginRuntimeOptions();
        var catalog = new PluginCatalog([], locationFailures);

        builder
            .AddApplicationPart(typeof(PluginCatalog).Assembly)
            .AddApplicationPart(typeof(PluginCatalogBootstrapper).Assembly);

        builder.MauiAppBuilder
            .UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton<IQuantumPluginEnvironment>(catalog);
        builder.Services.AddSingleton(runtimeOptions);
        builder.Services.AddSingleton<IPluginReferenceRelease, BlazorPluginReferenceRelease>();
        builder.Services.AddSingleton<PluginRuntimeManager>(services => new PluginRuntimeManager(
            catalog,
            runtimeOptions,
            services.GetRequiredService<IPluginReferenceRelease>(),
            logger: services.GetRequiredService<ILogger<PluginRuntimeManager>>()));
        builder.Services.AddSingleton<IPluginRuntimeManager>(services =>
            services.GetRequiredService<PluginRuntimeManager>());
        builder.Services.AddSingleton<PluginStaticAssetFileProvider>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<IComponentActivator, PluginComponentActivator>();
        builder.Services.AddInitializationStep(new PluginLifecycleInitializationStep());

        var nofApp = builder.BuildAsync().GetAwaiter().GetResult();
        return nofApp.MauiApp;
    }

    private static (PluginRuntimeOptions Options, IReadOnlyList<PluginLoadFailure> Failures)
        ResolvePluginRuntimeOptions()
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
            return (
                new PluginRuntimeOptions(
                    preferredPath,
                    Path.Combine(FileSystem.CacheDirectory, "PluginShadow")),
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (string.Equals(preferredPath, applicationDataPath, StringComparison.Ordinal))
            {
                return (
                    new PluginRuntimeOptions(
                        applicationDataPath,
                        Path.Combine(FileSystem.CacheDirectory, "PluginShadow")),
                    [new PluginLoadFailure(
                        null,
                        $"Plugin directory '{preferredPath}' is not accessible.",
                        exception)]);
            }

            Directory.CreateDirectory(applicationDataPath);
            return (
                new PluginRuntimeOptions(
                    applicationDataPath,
                    Path.Combine(FileSystem.CacheDirectory, "PluginShadow")),
                [new PluginLoadFailure(
                    null,
                    $"Plugin directory '{preferredPath}' is not accessible; using application data instead.",
                    exception)]);
        }
    }

}
