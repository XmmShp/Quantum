using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;
using NOF.Hosting;
using NOF.Hosting.Maui;
using Quantum.Logging;
using Quantum.Plugins;
using Quantum.WebPlugins;

namespace Quantum;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var loggingOptions = QuantumLoggingOptions.Parse(
            Environment.GetCommandLineArgs(),
            FileSystem.AppDataDirectory);
        var consoleOutputRequested = loggingOptions.WriteToConsole;
#if WINDOWS
        if (consoleOutputRequested && !WindowsConsole.TryEnable())
        {
            loggingOptions = loggingOptions with { WriteToConsole = false };
        }
#endif
        var builder = NOFMauiAppBuilder.Create();
        QuantumLogging.Configure(builder.Logging, loggingOptions);
        var (runtimeOptions, locationFailures) = ResolvePluginRuntimeOptions();

        builder
            .AddApplicationPart(typeof(PluginCatalog).Assembly)
            .AddApplicationPart(typeof(PluginCatalogBootstrapper).Assembly);

        builder.MauiAppBuilder
            .UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        builder.Services.AddSingleton<PluginCatalog>(services => new PluginCatalog(
            [],
            locationFailures,
            services.GetRequiredService<ILogger<PluginCatalog>>()));
        builder.Services.AddSingleton<IQuantumPluginEnvironment>(services =>
            services.GetRequiredService<PluginCatalog>());
        builder.Services.AddQuantumPluginEventBus();
        builder.Services.AddSingleton(runtimeOptions);
        builder.Services.AddSingleton<IPluginReferenceRelease, BlazorPluginReferenceRelease>();
        builder.Services.AddSingleton<PluginRuntimeManager>(services => new PluginRuntimeManager(
            services.GetRequiredService<PluginCatalog>(),
            runtimeOptions,
            services.GetRequiredService<IPluginReferenceRelease>(),
            logger: services.GetRequiredService<ILogger<PluginRuntimeManager>>()));
        builder.Services.AddSingleton<IPluginRuntimeManager>(services =>
            services.GetRequiredService<PluginRuntimeManager>());
        builder.Services.AddSingleton<PluginStaticAssetFileProvider>();
        builder.Services.AddScoped<WebPluginInteropBridge>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<IComponentActivator, PluginComponentActivator>();
        builder.Services.AddInitializationStep(new PluginLifecycleInitializationStep());

        var nofApp = builder.BuildAsync().GetAwaiter().GetResult();
        nofApp.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Quantum.Startup")
            .LogInformation(
                "Logging initialized. Process log file starts at {LogFilePath}; "
                + "console requested: {ConsoleOutputRequested}; console enabled: {ConsoleOutputEnabled}.",
                loggingOptions.GetFilePath(segmentIndex: 0),
                consoleOutputRequested,
                loggingOptions.WriteToConsole);
        return nofApp.MauiApp;
    }

    private static (PluginRuntimeOptions Options, IReadOnlyList<PluginLoadFailure> Failures)
        ResolvePluginRuntimeOptions()
    {
        var configuredPath = Environment.GetEnvironmentVariable("QUANTUM_MODULES_PATH");
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Modules");
        var applicationDataPath = Path.Combine(FileSystem.AppDataDirectory, "Modules");
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "quantum.db");
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
                    Path.Combine(FileSystem.CacheDirectory, "PluginShadow"),
                    databasePath),
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (string.Equals(preferredPath, applicationDataPath, StringComparison.Ordinal))
            {
                return (
                    new PluginRuntimeOptions(
                        applicationDataPath,
                        Path.Combine(FileSystem.CacheDirectory, "PluginShadow"),
                        databasePath),
                    [new PluginLoadFailure(
                        null,
                        $"Plugin directory '{preferredPath}' is not accessible.",
                        exception)]);
            }

            Directory.CreateDirectory(applicationDataPath);
            return (
                    new PluginRuntimeOptions(
                        applicationDataPath,
                        Path.Combine(FileSystem.CacheDirectory, "PluginShadow"),
                        databasePath),
                [new PluginLoadFailure(
                    null,
                    $"Plugin directory '{preferredPath}' is not accessible; using application data instead.",
                    exception)]);
        }
    }

}
