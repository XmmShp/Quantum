using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class PluginRpcJsonContractTests
{
    [Fact]
    public void NOFDefaultsAndObjectSerializerUseTheHostStringEnumContract()
    {
        Assert.Equal("\"Income\"", JsonSerializer.Serialize(JsonContractKind.Income, JsonSerializerOptions.NOF));
        Assert.Equal(JsonContractKind.Income, JsonSerializer.Deserialize<JsonContractKind>("\"Income\"", JsonSerializerOptions.NOF));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JsonContractKind>("1", JsonSerializerOptions.NOF));
        var serializer = new NOF.Infrastructure.JsonObjectSerializer();
        var payload = serializer.Serialize(JsonContractKind.Income);
        Assert.Equal("\"Income\"", System.Text.Encoding.UTF8.GetString(payload.Span));
        Assert.Equal(JsonContractKind.Income, serializer.Deserialize(payload, typeof(JsonContractKind)));
    }

    [Fact]
    public async Task CatalogAndDispatchAgreeOnStringEnumsAndOptionalConstructorDefaults()
    {
        await using var fixture = CreateFixture();
        var catalog = await fixture.Call("quantum.rpc.catalog", "{}");
        var methods = catalog.SerializedResult!.Value.GetProperty("value").GetProperty("services")
            .EnumerateArray().Single(service => service.GetProperty("serviceName").GetString() == "json-contract")
            .GetProperty("methods");
        var method = Assert.Single(methods.EnumerateArray());
        foreach (var schema in new[] { method.GetProperty("inputSchema"), method.GetProperty("outputSchema") })
        {
            var properties = schema.GetProperty("properties");
            Assert.Equal("string", properties.GetProperty("kind").GetProperty("type").GetString());
            Assert.Equal(new[] { "Expense", "Income" }, properties.GetProperty("kind").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("numeric values are not accepted", properties.GetProperty("kind").GetProperty("description").GetString());
            var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("kind", required);
            Assert.DoesNotContain("note", required);
            Assert.DoesNotContain("relatedEntryIds", required);
            Assert.Equal("", properties.GetProperty("note").GetProperty("default").GetString());
        }

        var invocation = await fixture.Call("json-contract.create", """
            {"date":"2026-10-19","kind":"Expense","amount":0.01,"category":"测试"}
            """);
        Assert.Null(invocation.Failure);
        var saved = invocation.SerializedResult!.Value.GetProperty("value");
        Assert.Equal("Expense", saved.GetProperty("kind").GetString());
        Assert.Equal("", saved.GetProperty("note").GetString());
        Assert.Equal(1, fixture.Counter.Calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("\"0\"")]
    [InlineData("\"Unknown\"")]
    public async Task InvalidEnumsReturnFieldPathWithoutInvokingHandler(string kind)
    {
        await using var fixture = CreateFixture();
        var invocation = await fixture.Call("json-contract.create",
            "{\"date\":\"2026-10-19\",\"kind\":" + kind + ",\"amount\":0.01,\"category\":\"测试\",\"note\":\"private-note\"}");
        Assert.Equal("rpc_invalid_payload", invocation.Failure!.ErrorCode);
        Assert.Equal("$.kind", invocation.Failure.Extra["path"]);
        Assert.Contains("$.kind", invocation.Failure.Message);
        Assert.False(string.IsNullOrWhiteSpace(invocation.Failure.Extra["reason"]));
        Assert.DoesNotContain("private-note", invocation.Failure.Message);
        Assert.Equal(0, fixture.Counter.Calls);
    }

    [Theory]
    [InlineData("{\"date\":\"bad-date\",\"kind\":\"Expense\",\"amount\":1,\"category\":\"Test\"}", "$.date")]
    [InlineData("{\"date\":\"2026-10-19\",\"kind\":\"Expense\",\"amount\":\"bad-amount\",\"category\":\"Test\"}", "$.amount")]
    [InlineData("{\"date\":\"2026-10-19\",\"kind\":\"Expense\",\"amount\":1,\"category\":\"Test\",\"note\":null}", "$.note")]
    [InlineData("{\"date\":\"2026-10-19\",\"kind\":\"Expense\",\"amount\":1,\"category\":\"Test\",\"lines\":[{\"kind\":0}]}", "$.lines[0].kind")]
    [InlineData("null", "$")]
    public async Task InvalidFieldsReportPrecisePathsAndNeverInvokeTheHandler(string payload, string path)
    {
        await using var fixture = CreateFixture();
        var invocation = await fixture.Call("json-contract.create", payload);
        Assert.Equal("rpc_invalid_payload", invocation.Failure!.ErrorCode);
        Assert.Equal(path, invocation.Failure.Extra["path"]);
        Assert.Contains(path, invocation.Failure.Message);
        Assert.Equal(0, fixture.Counter.Calls);
    }

    [Fact]
    public async Task MissingRequiredConstructorFieldIsReportedAndTheNextCallCanRecover()
    {
        await using var fixture = CreateFixture();
        var invalid = await fixture.Call("json-contract.create", """
            {"date":"2026-10-19","amount":1,"category":"Test"}
            """);
        Assert.Equal("rpc_invalid_payload", invalid.Failure!.ErrorCode);
        Assert.Contains("kind", invalid.Failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Counter.Calls);
        var valid = await fixture.Call("json-contract.create", """
            {"date":"2026-10-19","kind":"Income","amount":1,"category":"Test"}
            """);
        Assert.Null(valid.Failure);
        Assert.Equal(1, fixture.Counter.Calls);
    }

    [Fact]
    public async Task DotNetInvokerUsesTheSameEnumWireContractForRequestsAndResponses()
    {
        await using var fixture = CreateFixture();
        using var serializer = new PluginRpcSerializer();
        using var invoker = new PluginRpcInvoker(PluginId.Of("quantum.plugin.caller"), Guid.NewGuid(), serializer);
        invoker.UseRegistry(fixture.Registry);
        var result = await invoker.InvokeAsync<JsonContractRequest>("json-contract.create",
            new JsonContractRequest(new(2026, 10, 19), JsonContractKind.Income, 1m, "Test"), Context.Empty);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(JsonContractKind.Income, result.Value!.Kind);
        Assert.Equal("", result.Value.Note);
    }

    [Fact]
    public void ExporterHonorsJsonNamesIgnoredMembersNullableEnumsAliasesAndRecursiveCollections()
    {
        using var serializer = new PluginRpcSerializer();
        var schema = serializer.GetSchema(typeof(JsonContractShape), new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        var properties = schema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("wire_name"));
        Assert.False(properties.ContainsKey("ignored"));
        Assert.Contains("null", properties["optionalKind"]!.ToJsonString());
        Assert.Contains("outgoing", properties["alias"]!.ToJsonString());
        Assert.Contains("Income", properties["values"]!["additionalProperties"]!.ToJsonString());
        Assert.Contains("$ref", schema.ToJsonString());
        var value = serializer.Deserialize<JsonContractShape>(JsonSerializer.SerializeToElement(new
        {
            wire_name = "label",
            alias = "outgoing",
            values = new { item = "Income" },
            optionalKind = (string?)null
        }));
        Assert.Equal("label", value!.Name);
        Assert.Equal(JsonContractKind.Income, value.Values["item"]);
    }

    private static Fixture CreateFixture()
    {
        var services = new ServiceCollection().AddQuantumPluginRpc().AddSingleton<JsonContractCounter>()
            .AddTransient<JsonContractRpcService.Create, JsonContractCreate>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var serializer = new PluginRpcSerializer();
        var runtime = PluginRpcRuntime.Create(PluginId.Of("quantum.plugin.json-contract"), Guid.NewGuid(),
            typeof(JsonContractRpcService).Assembly, services.GetRequiredService<IServiceScopeFactory>(), serializer, NullLogger.Instance);
        runtime.Resume();
        return new Fixture(services, serializer, runtime, PluginRpcRegistry.Create([runtime], NullLogger.Instance));
    }

    private sealed record Fixture(ServiceProvider Services, PluginRpcSerializer Serializer, PluginRpcRuntime Runtime,
        PluginRpcRegistry Registry) : IAsyncDisposable
    {
        public JsonContractCounter Counter => Services.GetRequiredService<JsonContractCounter>();
        public Task<PluginRpcDispatchResult> Call(string name, string json)
            => Registry.InvokeAsync(name, JsonSerializer.Deserialize<JsonElement>(json),
                new PluginRpcCallContext("quantum.plugin.caller", Guid.NewGuid().ToString("N"), new Dictionary<string, JsonElement>()),
                true, CancellationToken.None);
        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            await Services.DisposeAsync();
            Serializer.Dispose();
        }
    }
}

public enum JsonContractKind { Expense, Income }
public enum JsonContractAlias { [JsonStringEnumMemberName("outgoing")] Expense }
public sealed record JsonContractLine(JsonContractKind Kind, string Note = "");
public sealed record JsonContractRequest(DateOnly Date,
    [property: Description("0 is expense; 1 is income.")] JsonContractKind Kind,
    decimal Amount, string Category, string Note = "", Guid[]? RelatedEntryIds = null,
    JsonContractKind? OptionalKind = null, JsonContractLine[]? Lines = null);
public sealed class JsonContractShape
{
    [JsonPropertyName("wire_name")]
    public string Name { get; set; } = "";
    [JsonIgnore]
    public string Ignored { get; set; } = "";
    public JsonContractKind? OptionalKind { get; set; }
    public JsonContractAlias Alias { get; set; }
    public Dictionary<string, JsonContractKind> Values { get; set; } = [];
    public JsonContractShape? Next { get; set; }
}
public sealed class JsonContractCounter { public int Calls { get; set; } }
[TransportOverQuantum]
[RpcInvocationName("json-contract")]
public interface IJsonContractRpcService : IRpcService
{
    [RpcInvocationName("create")]
    Result<JsonContractRequest> Create(JsonContractRequest request);
}
public partial class JsonContractRpcService : RpcServer<IJsonContractRpcService>;
public sealed class JsonContractCreate(JsonContractCounter counter) : JsonContractRpcService.Create
{
    public override Task<Result<JsonContractRequest>> HandleAsync(JsonContractRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        counter.Calls++;
        return Task.FromResult<Result<JsonContractRequest>>(request);
    }
}
