using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using Quantum.Plugins;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

internal sealed class OfficialRpcHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly PluginRpcSerializer _serializer = new();
    private readonly PluginRpcRuntime _runtime;
    private readonly PluginRpcRegistry _registry;

    public OfficialRpcHarness(IServiceCollection services, string pluginId, Assembly assembly)
    {
        services.AddQuantumPluginRpc();
        foreach (var initializer in assembly.GetCustomAttributes<NOF.Abstraction.AssemblyInitializeAttribute>())
        {
            initializer.Initialize(services);
        }
        foreach (var server in assembly.GetTypes().Where(type => !type.IsAbstract && typeof(IRpcServer).IsAssignableFrom(type)))
        {
            var mappings = (IReadOnlyDictionary<string, RpcHandlerMapping>)server.GetProperty("HandlerMappings", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
            foreach (var mapping in mappings.Values)
            {
                Assert.Single(services, descriptor => descriptor.ServiceType == mapping.HandlerType);
            }
        }
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _runtime = PluginRpcRuntime.Create(PluginId.Of(pluginId), Guid.NewGuid(), assembly,
            _provider.GetRequiredService<IServiceScopeFactory>(), _serializer, NullLogger.Instance);
        _runtime.Resume();
        _registry = PluginRpcRegistry.Create([_runtime], NullLogger.Instance);
    }

    public async Task<JsonElement> Call(string method, object request, bool expectsValue = true, CancellationToken cancellationToken = default)
    {
        var response = await _registry.InvokeAsync(method, JsonSerializer.SerializeToElement(request, JsonSerializerOptions.NOF),
            new PluginRpcCallContext("quantum.plugin.audit", Guid.NewGuid().ToString("N"), new Dictionary<string, JsonElement>()), expectsValue, cancellationToken);
        Assert.Null(response.Failure);
        Assert.True(response.SerializedResult.HasValue);
        return response.SerializedResult.Value;
    }

    public async Task AssertCatalog(string pluginId, string serviceName, string[] expectedMethods)
    {
        var result = await Call("quantum.rpc.catalog", new
        {
        });
        var service = Assert.Single(result.GetProperty("value").GetProperty("services").EnumerateArray(),
            item => item.GetProperty("pluginId").GetString() == pluginId && item.GetProperty("serviceName").GetString() == serviceName);
        Assert.Equal(serviceName, service.GetProperty("serviceName").GetString());
        var methods = service.GetProperty("methods").EnumerateArray().ToArray();
        Assert.Equal(expectedMethods.Order(StringComparer.Ordinal), methods.Select(method =>
            method.GetProperty("canonicalName").GetString()!.Split('.')[1]).Order(StringComparer.Ordinal));
        foreach (var method in methods)
        {
            Assert.StartsWith(pluginId + "." + serviceName + ".", method.GetProperty("qualifiedName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("description").GetString()));
            Assert.Equal("object", method.GetProperty("inputSchema").GetProperty("type").GetString());
            AssertEnglishMetadata(method);
        }
    }

    private static void AssertEnglishMetadata(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                Assert.DoesNotContain(value.GetString()!, character => character is >= '\u4e00' and <= '\u9fff');
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    AssertEnglishMetadata(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    AssertEnglishMetadata(item);
                }
                break;
        }
    }

    public IRpcInvoker CreateInvoker()
    {
        var invoker = new PluginRpcInvoker(PluginId.Of("quantum.plugin.calendar"), Guid.NewGuid(), _serializer);
        invoker.UseRegistry(_registry);
        return invoker;
    }

    public async Task UseClient<TClient>(Func<TClient, Task> action) where TClient : notnull
    {
        await using var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await action(scope.ServiceProvider.GetRequiredService<TClient>());
    }

    public async Task UseServices(Func<IServiceProvider, CancellationToken, Task> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await action(scope.ServiceProvider, default);
    }

    public static JsonElement Value(JsonElement result)
    {
        Assert.True(result.GetProperty("isSuccess").GetBoolean(), result.ToString());
        return result.GetProperty("value");
    }

    public async ValueTask DisposeAsync()
    {
        await _runtime.DisposeAsync();
        _serializer.Dispose();
        await _provider.DisposeAsync();
    }
}
