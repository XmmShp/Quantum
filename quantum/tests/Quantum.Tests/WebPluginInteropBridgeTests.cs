using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Quantum.Plugins;
using Quantum.WebPlugins;

namespace Quantum.Tests;

public sealed class WebPluginInteropBridgeTests
{
    private const string PluginId = "quantum.plugin.web";
    private const string TargetPluginId = "quantum.plugin.target";
    private const string Topic = "example.web.status";

    [Fact]
    public async Task ReplacementPublishIgnoresDeliveryFailureFromStaleRuntime()
    {
        await using var host = CreateHost();
        var oldRuntimeId = Guid.NewGuid();
        var newRuntimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([LoadedWebPlugin(oldRuntimeId)]);
        var javaScript = new RejectingEventDeliveryJavaScriptRuntime();
        using var bridge = CreateBridge(catalog, host, javaScript);
        await SubscribeAsync(bridge, oldRuntimeId);

        catalog.Replace([LoadedWebPlugin(newRuntimeId)]);

        var result = await PublishAsync(bridge, newRuntimeId);

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
        Assert.Equal(1, javaScript.DispatchCount);
    }

    [Fact]
    public async Task ActiveRuntimePublishStillReportsDeliveryFailure()
    {
        await using var host = CreateHost();
        var runtimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([LoadedWebPlugin(runtimeId)]);
        var javaScript = new RejectingEventDeliveryJavaScriptRuntime();
        using var bridge = CreateBridge(catalog, host, javaScript);
        await SubscribeAsync(bridge, runtimeId);

        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await PublishAsync(bridge, runtimeId));

        Assert.Contains(exception.InnerExceptions, static failure => failure is JSException);
        Assert.Equal(1, javaScript.DispatchCount);
    }

    [Fact]
    public async Task DotNetInvocationDoesNotRequireAnIntegrationDeclaration()
    {
        await using var host = CreateHost();
        var runtimeId = Guid.NewGuid();
        var catalog = new PluginCatalog([
            LoadedWebPlugin(runtimeId),
            LoadedDotNetPlugin(host)
        ]);
        using var bridge = CreateBridge(catalog, host, new RejectingEventDeliveryJavaScriptRuntime());

        var result = await bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "dotnet",
            "invoke",
            JsonSerializer.SerializeToElement(new
            {
                target = TargetPluginId,
                service = typeof(TestInteropService).FullName,
                method = nameof(TestInteropService.Echo),
                arguments = new[] { "hello" },
                parameterTypes = new[] { typeof(string).FullName }
            }));

        Assert.Equal("echo:hello", result.GetString());
    }

    private static ServiceProvider CreateHost()
        => new ServiceCollection()
            .AddQuantumPluginEventBus()
            .AddSingleton<TestInteropService>()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

    private static WebPluginInteropBridge CreateBridge(
        PluginCatalog catalog,
        ServiceProvider host,
        IJSRuntime javaScript)
        => new(
            catalog,
            host,
            new TestNavigationManager(),
            javaScript,
            host.GetRequiredService<QuantumPluginEventBusFactory>(),
            NullLogger<WebPluginInteropBridge>.Instance);

    private static Task<JsonElement> SubscribeAsync(
        WebPluginInteropBridge bridge,
        Guid runtimeId)
        => bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "eventBus",
            "subscribe",
            JsonSerializer.SerializeToElement(new
            {
                topic = Topic,
                subscriptionId = "subscription"
            }));

    private static Task<JsonElement> PublishAsync(
        WebPluginInteropBridge bridge,
        Guid runtimeId)
        => bridge.InvokeAsync(
            PluginId,
            runtimeId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            "eventBus",
            "publish",
            JsonSerializer.SerializeToElement(new
            {
                topic = Topic,
                payload = new { state = "activated" }
            }));

    private static LoadedPlugin LoadedWebPlugin(Guid runtimeId)
        => new(
            new PluginManifest(
                Quantum.Plugin.Abstraction.PluginId.Of(PluginId),
                SemanticVersion.Of("1.0.0"),
                PluginRuntimeDefinition.Web("plugin.js")),
            Path.Combine("plugins", PluginId),
            entryAssembly: null,
            routes: [],
            runtimeId);

    private static LoadedPlugin LoadedDotNetPlugin(IServiceProvider services)
        => new(
            new PluginManifest(
                Quantum.Plugin.Abstraction.PluginId.Of(TargetPluginId),
                SemanticVersion.Of("1.0.0"),
                "Target.dll"),
            Path.Combine("plugins", TargetPluginId),
            typeof(WebPluginInteropBridgeTests).Assembly,
            routes: [],
            runtimeId: Guid.NewGuid(),
            services: services);

    private sealed class TestInteropService
    {
        public string Echo(string value) => $"echo:{value}";
    }

    private sealed class RejectingEventDeliveryJavaScriptRuntime : IJSRuntime
    {
        public int DispatchCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Assert.Equal("quantum.plugins.dispatchEvent", identifier);
            DispatchCount++;
            return ValueTask.FromException<TValue>(new JSException("delivery failed"));
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://quantum.local/", "https://quantum.local/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}
