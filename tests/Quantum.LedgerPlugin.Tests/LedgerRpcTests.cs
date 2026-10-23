using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quantum.OfficialPlugins.Ledger;
using Quantum.OfficialPlugins.Ledger.Rpc;
using Quantum.Plugins;
using System.Text.Json;
using Xunit;

namespace Quantum.LedgerPlugin.Tests;

public sealed class LedgerRpcTests
{
    [Fact]
    public async Task CatalogDescribesCluesAndCsvMethodsAreRemoved()
    {
        await using var host = await Harness.Create();
        var catalog = await host.Call("quantum.rpc.catalog", new
        {
        });
        var service = Assert.Single(catalog.GetProperty("value").GetProperty("services").EnumerateArray(),
            item => item.GetProperty("pluginId").GetString() == "quantum.plugin.ledger");
        var methods = service.GetProperty("methods").EnumerateArray().ToArray();
        Assert.Equal(new[] { "by-calendar-entry", "create", "delete", "get", "list", "mobile-create", "mobile-refresh", "related", "summary", "update" },
            methods.Select(method => method.GetProperty("canonicalName").GetString()!.Split('.')[1]).Order(StringComparer.Ordinal));
        foreach (var method in methods)
        {
            Assert.StartsWith("quantum.plugin.ledger.ledger.", method.GetProperty("qualifiedName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("description").GetString()));
            Assert.Equal("object", method.GetProperty("inputSchema").GetProperty("type").GetString());
        }
        var create = methods.Single(method => method.GetProperty("canonicalName").GetString() == "ledger.create");
        Assert.True(create.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("relatedEntryIds", out _));
        Assert.Equal("string", create.GetProperty("inputSchema").GetProperty("properties").GetProperty("kind").GetProperty("type").GetString());
        Assert.False(create.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("relatedExpenseId", out _));
        Assert.NotNull((await host.Dispatch("ledger.export-csv", new
        {
            month = "2026-10-01"
        })).Failure);
        Assert.NotNull((await host.Dispatch("ledger.import-csv", new
        {
            csv = "unused"
        })).Failure);
    }

    [Fact]
    public async Task HostDispatchPreservesExactCrudMonthTotalsAndCategoryStatistics()
    {
        await using var host = await Harness.Create();
        var expense = await Create(host, EntryKind.Expense, new(2026, 10, 18), 25.50m, "餐饮");
        await Create(host, EntryKind.Income, new(2026, 10, 1), 6000m, "工资");
        var month = new LedgerMonthRequest
        {
            Month = new(2026, 10, 18)
        };
        Assert.Equal(2, (await host.Call("ledger.list", month)).GetProperty("value").GetArrayLength());
        var summary = (await host.Call("ledger.summary", month)).GetProperty("value");
        Assert.Equal(5974.50m, summary.GetProperty("balance").GetDecimal());
        Assert.Equal("CNY", summary.GetProperty("currency").GetString());
        Assert.Equal("餐饮", summary.GetProperty("expenseCategories")[0].GetProperty("category").GetString());
        AssertSuccess(await host.Call("ledger.update", new UpdateLedgerEntryRequest
        {
            Id = expense,
            Entry = new()
            {
                Date = new(2026, 11, 1),
                Kind = EntryKind.Expense,
                Amount = 30.25m,
                Category = "交通",
                Note = "Moved"
            }
        }));
        Assert.Single((await host.Call("ledger.list", month)).GetProperty("value").EnumerateArray());
        Assert.Equal(30.25m, (await Get(host, expense)).GetProperty("amount").GetDecimal());
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest
        {
            Id = expense
        }, expectsValue: false));
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest
        {
            Id = expense
        }, expectsValue: false));
        Assert.Equal(JsonValueKind.Null, (await host.Call("ledger.get", new LedgerEntryRequest
        {
            Id = expense
        })).GetProperty("value").GetProperty("entry").ValueKind);
    }

    [Theory]
    [InlineData(EntryKind.Expense, EntryKind.Expense)]
    [InlineData(EntryKind.Expense, EntryKind.Income)]
    [InlineData(EntryKind.Income, EntryKind.Expense)]
    [InlineData(EntryKind.Income, EntryKind.Income)]
    public async Task AnyTypeCanLinkAcrossMonthsAndChangingTypeDoesNotBreakTheClue(EntryKind firstKind, EntryKind secondKind)
    {
        await using var host = await Harness.Create();
        var first = await Create(host, firstKind, new(2026, 9, 1));
        var second = await Create(host, secondKind, new(2026, 10, 1), related: [first]);
        Assert.Equal(new[] { second }, await Related(host, first));
        Assert.Equal(new[] { first }, await Related(host, second));
        Assert.Equal(first, Assert.Single((await Get(host, second)).GetProperty("relatedEntryIds").EnumerateArray()).GetGuid());
        var summaryBefore = (await host.Call("ledger.summary", new LedgerMonthRequest
        {
            Month = new(2026, 10, 1)
        })).GetProperty("value");
        Assert.Equal(secondKind == EntryKind.Income ? 10m : 0m, summaryBefore.GetProperty("income").GetDecimal());
        Assert.Equal(secondKind == EntryKind.Expense ? 10m : 0m, summaryBefore.GetProperty("expense").GetDecimal());
        AssertSuccess(await host.Call("ledger.update", new UpdateLedgerEntryRequest
        {
            Id = first,
            Entry = new()
            {
                Date = new(2026, 9, 1),
                Kind = firstKind == EntryKind.Expense ? EntryKind.Income : EntryKind.Expense,
                Amount = 10m,
                Category = "Changed",
                Note = "",
                RelatedEntryIds = [second]
            }
        }));
        Assert.Equal(new[] { first }, await Related(host, second));
        Assert.Equal(summaryBefore.GetRawText(), (await host.Call("ledger.summary", new LedgerMonthRequest
        {
            Month = new(2026, 10, 1)
        })).GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task MultipleSymmetricCluesAreDeduplicatedAndUnlinkOrDeleteNeverDeletesOtherRecords()
    {
        await using var host = await Harness.Create();
        var first = await Create(host, EntryKind.Expense, new(2026, 9, 1));
        var second = await Create(host, EntryKind.Income, new(2026, 10, 1), related: [first, first]);
        var third = await Create(host, EntryKind.Expense, new(2026, 11, 1), related: [first, second]);
        Assert.Equal(new[] { second, third }.Order(), (await Related(host, first)).Order());
        Assert.Equal(new[] { first, third }.Order(), (await Related(host, second)).Order());
        // Replacing either endpoint with the same list does not duplicate links.
        AssertSuccess(await host.Call("ledger.update", new UpdateLedgerEntryRequest
        {
            Id = second,
            Entry = new()
            {
                Date = new(2026, 10, 1),
                Kind = EntryKind.Income,
                Amount = 10m,
                Category = "Updated",
                Note = "",
                RelatedEntryIds = [first, third]
            }
        }));
        Assert.Equal(2, (await Related(host, first)).Length);
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest
        {
            Id = first
        }, expectsValue: false));
        Assert.Equal(new[] { third }, await Related(host, second));
        Assert.Equal(new[] { second }, await Related(host, third));
        Assert.Equal(10m, (await Get(host, second)).GetProperty("amount").GetDecimal());
        // Omitting relatedEntryIds replaces the full relation set with empty.
        AssertSuccess(await host.Call("ledger.update", new UpdateLedgerEntryRequest
        {
            Id = second,
            Entry = new()
            {
                Date = new(2026, 10, 1),
                Kind = EntryKind.Income,
                Amount = 10m,
                Category = "Updated"
            }
        }));
        Assert.Empty(await Related(host, second));
        Assert.Empty(await Related(host, third));
        Assert.Equal(10m, (await Get(host, third)).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task InvalidRelationsAreAtomicAndCancelledRequestsDoNotWrite()
    {
        await using var host = await Harness.Create();
        var first = await Create(host, EntryKind.Expense, new(2026, 10, 1));
        var second = await Create(host, EntryKind.Expense, new(2026, 10, 2), related: [first]);
        foreach (var (ids, code) in new[] { (new[] { second }, "ledger_invalid_request"), (new[] { Guid.Empty }, "ledger_invalid_request"), (new[] { first, Guid.NewGuid() }, "ledger_entry_not_found") })
        {
            var result = await host.Call("ledger.update", new UpdateLedgerEntryRequest
            {
                Id = second,
                Entry = new()
                {
                    Date = new(2026, 12, 1),
                    Kind = EntryKind.Income,
                    Amount = 999m,
                    Category = "Invalid",
                    Note = "",
                    RelatedEntryIds = ids
                }
            });
            Assert.Equal(code, result.GetProperty("errorCode").GetString());
            Assert.Equal(new[] { first }, await Related(host, second));
            var unchanged = await Get(host, second);
            Assert.Equal(10m, unchanged.GetProperty("amount").GetDecimal());
            Assert.Equal("Expense", unchanged.GetProperty("kind").GetString());
        }
        var missing = await host.Call("ledger.create", new SaveLedgerEntryRequest
        {
            Date = new(2026, 10, 1),
            Kind = EntryKind.Income,
            Amount = 1m,
            Category = "Missing",
            Note = "",
            RelatedEntryIds = [Guid.NewGuid()]
        });
        Assert.Equal("ledger_entry_not_found", missing.GetProperty("errorCode").GetString());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("ledger.update", new UpdateLedgerEntryRequest
        {
            Id = second,
            Entry = new()
            {
                Date = new(2026, 10, 2),
                Kind = EntryKind.Income,
                Amount = 1m,
                Category = "Cancelled",
                Note = "",
                RelatedEntryIds = []
            }
        }, cancellationToken: cancelled.Token));
        Assert.Equal(new[] { first }, await Related(host, second));
        Assert.Equal(2, (await host.Call("ledger.list", new LedgerMonthRequest
        {
            Month = new(2026, 10, 1)
        })).GetProperty("value").GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.001)]
    public async Task InvalidAmountsReturnBusinessErrorsWithoutWriting(decimal amount)
    {
        await using var host = await Harness.Create();
        var result = await host.Call("ledger.create", new SaveLedgerEntryRequest
        {
            Date = new(2026, 10, 18),
            Kind = EntryKind.Expense,
            Amount = amount,
            Category = "Food"
        });
        Assert.Equal("ledger_invalid_request", result.GetProperty("errorCode").GetString());
        Assert.Empty((await host.Call("ledger.list", new LedgerMonthRequest
        {
            Month = new(2026, 10, 1)
        })).GetProperty("value").EnumerateArray());
    }

    [Theory]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":0,\"amount\":1,\"category\":\"Food\"}")]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":\"Unknown\",\"amount\":1,\"category\":\"Food\"}")]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":\"Expense\",\"amount\":1,\"category\":null}")]
    public async Task InvalidWireTypesReturnHostErrorsWithoutWriting(string json)
    {
        await using var host = await Harness.Create();
        var result = await host.Dispatch("ledger.create", JsonSerializer.Deserialize<JsonElement>(json));
        Assert.Equal("rpc_invalid_payload", result.Failure!.ErrorCode);
        Assert.Empty((await host.Call("ledger.list", new LedgerMonthRequest
        {
            Month = new(2026, 10, 1)
        })).GetProperty("value").EnumerateArray());
    }

    [Fact]
    public async Task MissingRecordsInvalidIdsAndNullPayloadHaveExplicitErrors()
    {
        await using var host = await Harness.Create();
        Assert.Equal("ledger_entry_not_found", (await host.Call("ledger.related", new LedgerEntryRequest
        {
            Id = Guid.NewGuid()
        })).GetProperty("errorCode").GetString());
        Assert.Equal("ledger_entry_not_found", (await host.Call("ledger.update", new UpdateLedgerEntryRequest
        {
            Id = Guid.NewGuid(),
            Entry = new()
            {
                Date = new(2026, 10, 1),
                Kind = EntryKind.Income,
                Amount = 1m,
                Category = "Missing"
            }
        })).GetProperty("errorCode").GetString());
        Assert.Equal("ledger_invalid_request", (await host.Call("ledger.list", new LedgerMonthRequest
        {
            Month = default
        })).GetProperty("errorCode").GetString());
        Assert.Equal("ledger_invalid_request", (await host.Call("ledger.get", new LedgerEntryRequest
        {
            Id = Guid.Empty
        })).GetProperty("errorCode").GetString());
        Assert.NotNull((await host.Dispatch("ledger.create", null)).Failure);
    }

    private static async Task<Guid> Create(Harness host, EntryKind kind, DateOnly date, decimal amount = 10m, string category = "Entry", Guid[]? related = null)
    {
        var result = await host.Call("quantum.plugin.ledger.ledger.create", new SaveLedgerEntryRequest
        {
            Date = date,
            Kind = kind,
            Amount = amount,
            Category = category,
            Note = "",
            RelatedEntryIds = related
        });
        AssertSuccess(result);
        return result.GetProperty("value").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Get(Harness host, Guid id)
        => (await host.Call("ledger.get", new LedgerEntryRequest
        {
            Id = id
        })).GetProperty("value").GetProperty("entry");

    private static async Task<Guid[]> Related(Harness host, Guid id)
        => (await host.Call("ledger.related", new LedgerEntryRequest
        {
            Id = id
        })).GetProperty("value").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid()).ToArray();

    private static void AssertSuccess(JsonElement result) => Assert.True(result.GetProperty("isSuccess").GetBoolean(), result.ToString());

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly PluginRpcRuntime _runtime;
        private readonly PluginRpcSerializer _serializer;
        private readonly PluginRpcRegistry _registry;

        public Harness(
            SqliteConnection connection,
            ServiceProvider provider,
            PluginRpcRuntime runtime,
            PluginRpcSerializer serializer,
            PluginRpcRegistry registry)
        {
            _connection = connection;
            _provider = provider;
            _runtime = runtime;
            _serializer = serializer;
            _registry = registry;
        }

        public static async Task<Harness> Create()
        {
            var connection = new SqliteConnection($"Data Source=ledger-rpc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await connection.OpenAsync();
            await LedgerPersistenceTests.Migrate(connection);
            var builder = LedgerPersistenceTests.Configure(connection.ConnectionString);
            var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var serializer = new PluginRpcSerializer();
            var runtime = PluginRpcRuntime.Create(PluginId.Of("quantum.plugin.ledger"), Guid.NewGuid(), typeof(ILedgerRpcService).Assembly,
                provider.GetRequiredService<IServiceScopeFactory>(), serializer, NullLogger.Instance);
            runtime.Resume();
            return new(connection, provider, runtime, serializer, PluginRpcRegistry.Create([runtime], NullLogger.Instance));
        }

        public async Task<JsonElement> Call(string method, object? request, bool expectsValue = true, CancellationToken cancellationToken = default)
        {
            var response = await Dispatch(method, request, expectsValue, cancellationToken);
            Assert.Null(response.Failure);
            Assert.True(response.SerializedResult.HasValue);
            return response.SerializedResult.Value;
        }

        public Task<PluginRpcDispatchResult> Dispatch(string method, object? request, bool expectsValue = true, CancellationToken cancellationToken = default)
            => _registry.InvokeAsync(method, JsonSerializer.SerializeToElement(request, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }),
                new PluginRpcCallContext("quantum.plugin.test-caller", Guid.NewGuid().ToString("N"), new Dictionary<string, JsonElement>()), expectsValue, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _runtime.DisposeAsync();
            _serializer.Dispose();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
