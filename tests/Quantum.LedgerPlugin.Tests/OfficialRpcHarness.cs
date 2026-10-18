using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quantum.Plugins;
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
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _runtime = PluginRpcRuntime.Create(PluginId.Of(pluginId), Guid.NewGuid(), assembly,
            _provider.GetRequiredService<IServiceScopeFactory>(), _serializer, NullLogger.Instance);
        _runtime.Resume();
        _registry = PluginRpcRegistry.Create([_runtime], NullLogger.Instance);
    }

    public async Task<JsonElement> Call(string method, object request, bool expectsValue = true, CancellationToken cancellationToken = default)
    {
        var response = await _registry.InvokeAsync(method, JsonSerializer.SerializeToElement(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new PluginRpcCallContext("quantum.plugin.audit", Guid.NewGuid().ToString("N"), new Dictionary<string, JsonElement>()), expectsValue, cancellationToken);
        Assert.Null(response.Failure);
        Assert.True(response.SerializedResult.HasValue);
        return response.SerializedResult.Value;
    }

    public async Task AssertCatalog(string pluginId, string serviceName, string[] expectedMethods)
    {
        var result = await Call("quantum.rpc.catalog", new { });
        var service = Assert.Single(result.GetProperty("value").GetProperty("services").EnumerateArray(),
            item => item.GetProperty("pluginId").GetString() == pluginId);
        Assert.Equal(serviceName, service.GetProperty("serviceName").GetString());
        var methods = service.GetProperty("methods").EnumerateArray().ToArray();
        Assert.Equal(expectedMethods.Order(StringComparer.Ordinal), methods.Select(method =>
            method.GetProperty("canonicalName").GetString()!.Split('.')[1]).Order(StringComparer.Ordinal));
        foreach (var method in methods)
        {
            Assert.StartsWith(pluginId + "." + serviceName + ".", method.GetProperty("qualifiedName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("description").GetString()));
            Assert.Equal("object", method.GetProperty("inputSchema").GetProperty("type").GetString());
        }
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
