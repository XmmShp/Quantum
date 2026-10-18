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
    public async Task HostCatalogDiscoversAllLedgerMethodsWithDescriptionsAndSchemas()
    {
        await using var host = await Harness.Create();
        var catalog = await host.Call("quantum.rpc.catalog", new
        {
        });
        var service = Assert.Single(catalog.GetProperty("value").GetProperty("services").EnumerateArray(),
            item => item.GetProperty("pluginId").GetString() == "quantum.plugin.ledger");
        Assert.Equal("ledger", service.GetProperty("serviceName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(service.GetProperty("description").GetString()));
        var methods = service.GetProperty("methods").EnumerateArray().ToArray();
        Assert.Equal(new[] { "create", "delete", "export-csv", "get", "import-csv", "list", "related", "summary", "update" },
            methods.Select(method => method.GetProperty("canonicalName").GetString()!.Split('.')[1]).Order(StringComparer.Ordinal));
        foreach (var method in methods)
        {
            Assert.StartsWith("quantum.plugin.ledger.ledger.", method.GetProperty("qualifiedName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(method.GetProperty("description").GetString()));
            Assert.Equal("object", method.GetProperty("inputSchema").GetProperty("type").GetString());
        }
        var create = methods.Single(method => method.GetProperty("canonicalName").GetString() == "ledger.create");
        Assert.True(create.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("relatedExpenseId", out _));
        Assert.False(create.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("relationKind", out _));
        Assert.Contains("人民币", create.GetProperty("inputSchema").GetProperty("properties").GetProperty("amount").GetProperty("description").GetString());
        Assert.Contains("ledger", create.GetProperty("outputSchema").GetRawText().ToLowerInvariant());
    }

    [Fact]
    public async Task HostDispatchCanCreateReadSummarizeUpdateExportImportAndDeleteRealRecords()
    {
        await using var host = await Harness.Create();
        var create = await host.Call("quantum.plugin.ledger.ledger.create",
            new SaveLedgerEntryRequest(new(2026, 10, 18), EntryKind.Expense, 25.50m, "餐饮", "RPC lunch"));
        AssertSuccess(create);
        var id = create.GetProperty("value").GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        var income = await host.Call("LEDGER.CREATE", new SaveLedgerEntryRequest(new(2026, 10, 1), EntryKind.Income, 6000m, "工资"));
        AssertSuccess(income);
        var month = new LedgerMonthRequest(new(2026, 10, 18));
        var list = await host.Call("ledger.list", month);
        AssertSuccess(list);
        Assert.Equal(2, list.GetProperty("value").GetArrayLength());
        var get = await host.Call("ledger.get", new LedgerEntryRequest(id));
        AssertSuccess(get);
        Assert.Equal("RPC lunch", get.GetProperty("value").GetProperty("entry").GetProperty("note").GetString());
        var summary = await host.Call("ledger.summary", month);
        AssertSuccess(summary);
        Assert.Equal(5974.50m, summary.GetProperty("value").GetProperty("balance").GetDecimal());
        Assert.Equal("CNY", summary.GetProperty("value").GetProperty("currency").GetString());
        Assert.Equal("2026-10-01", summary.GetProperty("value").GetProperty("month").GetString());
        Assert.Equal("餐饮", summary.GetProperty("value").GetProperty("expenseCategories")[0].GetProperty("category").GetString());
        var updated = await host.Call("ledger.update", new UpdateLedgerEntryRequest(id,
            new(new(2026, 11, 1), EntryKind.Expense, 30.25m, "交通", "Moved to November")));
        AssertSuccess(updated);
        Assert.Equal(id, updated.GetProperty("value").GetProperty("id").GetGuid());
        Assert.Equal(1, (await host.Call("ledger.list", month)).GetProperty("value").GetArrayLength());
        var export = await host.Call("ledger.export-csv", new LedgerMonthRequest(new(2026, 11, 1)));
        AssertSuccess(export);
        Assert.Equal("quantum-ledger-2026-11.csv", export.GetProperty("value").GetProperty("fileName").GetString());
        var csv = export.GetProperty("value").GetProperty("csv").GetString()!;
        var import = await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(csv));
        AssertSuccess(import);
        Assert.Equal(0, import.GetProperty("value").GetProperty("addedCount").GetInt32());
        var fresh = LedgerEntry.Create(Guid.NewGuid(), new(2026, 11, 2), EntryKind.Expense, .1m, "New", "");
        var conflict = LedgerEntry.Create(id, new(2026, 11, 1), EntryKind.Expense, 999m, "交通", "Conflict");
        var rejected = await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(LedgerCsv.Export([fresh, conflict])));
        Assert.Equal("ledger_invalid_request", rejected.GetProperty("errorCode").GetString());
        Assert.Equal(1, (await host.Call("ledger.list", new LedgerMonthRequest(new(2026, 11, 1)))).GetProperty("value").GetArrayLength());
        var imported = await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(LedgerCsv.Export([fresh])));
        AssertSuccess(imported);
        Assert.Equal(1, imported.GetProperty("value").GetProperty("addedCount").GetInt32());
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest(id), expectsValue: false));
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest(id), expectsValue: false));
        Assert.Equal(JsonValueKind.Null, (await host.Call("ledger.get", new LedgerEntryRequest(id))).GetProperty("value").GetProperty("entry").ValueKind);
    }

    [Fact]
    public async Task RelationsPersistAcrossMonthsAndCanBeChangedAndRemoved()
    {
        await using var host = await Harness.Create();
        var expense = await host.Call("quantum.plugin.ledger.ledger.create",
            new SaveLedgerEntryRequest(new(2026, 9, 1), EntryKind.Expense, 120m, "Dinner"));
        var expenseId = expense.GetProperty("value").GetProperty("id").GetGuid();
        var created = await host.Call("quantum.plugin.ledger.ledger.create",
            new SaveLedgerEntryRequest(new(2026, 10, 1), EntryKind.Income, 40m, "Repayment", "Alice", expenseId));
        AssertSuccess(created);
        var incomeId = created.GetProperty("value").GetProperty("id").GetGuid();
        AssertSuccess(await host.Call("quantum.plugin.ledger.ledger.create",
            new SaveLedgerEntryRequest(new(2026, 11, 1), EntryKind.Income, 30m, "Repayment", "Bob", expenseId)));
        var related = await host.Call("quantum.plugin.ledger.ledger.related", new LedgerEntryRequest(expenseId));
        Assert.Equal(2, related.GetProperty("value").GetArrayLength());
        Assert.Equal(70m, related.GetProperty("value").EnumerateArray().Sum(item => item.GetProperty("amount").GetDecimal()));
        var origin = await host.Call("quantum.plugin.ledger.ledger.related", new LedgerEntryRequest(incomeId));
        Assert.Equal(expenseId, Assert.Single(origin.GetProperty("value").EnumerateArray()).GetProperty("id").GetGuid());
        var persisted = (await host.Call("ledger.get", new LedgerEntryRequest(incomeId))).GetProperty("value").GetProperty("entry");
        Assert.Equal(expenseId, persisted.GetProperty("relatedExpenseId").GetGuid());
        Assert.False(persisted.TryGetProperty("relationKind", out _));
        var summary = (await host.Call("ledger.summary", new LedgerMonthRequest(new(2026, 10, 1)))).GetProperty("value");
        Assert.Equal(40m, summary.GetProperty("income").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("expense").GetDecimal());
        var blocked = await host.Call("ledger.delete", new LedgerEntryRequest(expenseId), expectsValue: false);
        Assert.Equal("ledger_invalid_request", blocked.GetProperty("errorCode").GetString());
        blocked = await host.Call("ledger.update", new UpdateLedgerEntryRequest(expenseId,
            new(new(2026, 9, 1), EntryKind.Income, 120m, "Changed")));
        Assert.Equal("ledger_invalid_request", blocked.GetProperty("errorCode").GetString());
        Assert.Equal(0, (await host.Call("ledger.get", new LedgerEntryRequest(expenseId))).GetProperty("value").GetProperty("entry").GetProperty("kind").GetInt32());
        var otherExpense = await host.Call("ledger.create", new SaveLedgerEntryRequest(new(2026, 9, 2), EntryKind.Expense, 80m, "Taxi"));
        var otherExpenseId = otherExpense.GetProperty("value").GetProperty("id").GetGuid();
        AssertSuccess(await host.Call("ledger.update", new UpdateLedgerEntryRequest(incomeId,
            new(new(2026, 10, 1), EntryKind.Income, 40m, "Repayment", "Alice", otherExpenseId))));
        Assert.Equal(otherExpenseId, Assert.Single((await host.Call("ledger.related", new LedgerEntryRequest(incomeId))).GetProperty("value").EnumerateArray()).GetProperty("id").GetGuid());
        AssertSuccess(await host.Call("ledger.update", new UpdateLedgerEntryRequest(incomeId,
            new(new(2026, 10, 1), EntryKind.Income, 40m, "Repayment", "Alice"))));
        Assert.Empty((await host.Call("ledger.related", new LedgerEntryRequest(incomeId))).GetProperty("value").EnumerateArray());
        var remaining = (await host.Call("ledger.related", new LedgerEntryRequest(expenseId))).GetProperty("value");
        var remainingId = Assert.Single(remaining.EnumerateArray()).GetProperty("id").GetGuid();
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest(remainingId), expectsValue: false));
        AssertSuccess(await host.Call("ledger.delete", new LedgerEntryRequest(expenseId), expectsValue: false));
    }

    [Fact]
    public async Task RelationValidationImportsAndCancellationCrossTheHostBoundary()
    {
        await using var host = await Harness.Create();
        var expense = LedgerEntry.Create(Guid.NewGuid(), new(2026, 9, 1), EntryKind.Expense, 60m, "Taxi", "");
        var income = LedgerEntry.Create(Guid.NewGuid(), new(2026, 10, 1), EntryKind.Income, 30m, "AA", "", expense.Id);
        // The origin may follow its repayment in the same atomic import.
        var csv = LedgerCsv.Export([income, expense]);
        var imported = await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(csv));
        AssertSuccess(imported);
        Assert.Equal(2, imported.GetProperty("value").GetProperty("addedCount").GetInt32());
        Assert.Equal(0, (await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(csv))).GetProperty("value").GetProperty("addedCount").GetInt32());
        Assert.Single((await host.Call("ledger.related", new LedgerEntryRequest(expense.Id))).GetProperty("value").EnumerateArray());
        var exported = (await host.Call("ledger.export-csv", new LedgerMonthRequest(new(2026, 10, 1)))).GetProperty("value").GetProperty("csv").GetString()!;
        Assert.True(income.SameContent(Assert.Single(LedgerCsv.Parse(exported))));
        var missing = await host.Call("ledger.create", new SaveLedgerEntryRequest(new(2026, 10, 1), EntryKind.Income, 1m, "AA", "", Guid.NewGuid()));
        Assert.Equal("ledger_entry_not_found", missing.GetProperty("errorCode").GetString());
        foreach (var request in new[] {
            new SaveLedgerEntryRequest(new(2026, 10, 1), EntryKind.Expense, 1m, "Bad", "", expense.Id),
            new SaveLedgerEntryRequest(new(2026, 10, 1), EntryKind.Income, 1m, "Bad", "", income.Id),
            new SaveLedgerEntryRequest(new(2026, 10, 1), EntryKind.Income, 1m, "Bad", "", Guid.Empty) })
        {
            Assert.Equal("ledger_invalid_request", (await host.Call("ledger.create", request)).GetProperty("errorCode").GetString());
        }

        var self = await host.Call("ledger.update", new UpdateLedgerEntryRequest(income.Id,
            new(new(2026, 10, 1), EntryKind.Income, 30m, "AA", "", income.Id)));
        Assert.Equal("ledger_invalid_request", self.GetProperty("errorCode").GetString());
        var fresh = LedgerEntry.Create(Guid.NewGuid(), new(2026, 10, 1), EntryKind.Expense, 10m, "Fresh", "");
        var dangling = LedgerEntry.Create(Guid.NewGuid(), new(2026, 10, 1), EntryKind.Income, 10m, "Bad", "", Guid.NewGuid());
        Assert.Equal("ledger_entry_not_found", (await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(LedgerCsv.Export([fresh, dangling])))).GetProperty("errorCode").GetString());
        Assert.Equal(JsonValueKind.Null, (await host.Call("ledger.get", new LedgerEntryRequest(fresh.Id))).GetProperty("value").GetProperty("entry").ValueKind);
        Assert.Equal("ledger_entry_not_found", (await host.Call("ledger.related", new LedgerEntryRequest(Guid.NewGuid()))).GetProperty("errorCode").GetString());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("quantum.plugin.ledger.ledger.related", new LedgerEntryRequest(expense.Id), cancellationToken: cancelled.Token));
    }

    [Theory]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":0,\"amount\":0,\"category\":\"Food\"}")]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":0,\"amount\":0.001,\"category\":\"Food\"}")]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":99,\"amount\":1,\"category\":\"Food\"}")]
    [InlineData("{\"date\":\"2026-10-18\",\"kind\":0,\"amount\":1,\"category\":null}")]
    public async Task InvalidRequestsReturnBusinessErrorsAndDoNotWrite(string json)
    {
        await using var host = await Harness.Create();
        var response = await host.Call("ledger.create", JsonSerializer.Deserialize<JsonElement>(json));
        Assert.False(response.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("ledger_invalid_request", response.GetProperty("errorCode").GetString());
        Assert.Equal(0, (await host.Call("ledger.list", new LedgerMonthRequest(new(2026, 10, 1)))).GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task MissingRecordsInvalidMonthsAndOversizeCsvHaveExplicitErrors()
    {
        await using var host = await Harness.Create();
        var missing = await host.Call("ledger.update", new UpdateLedgerEntryRequest(Guid.NewGuid(), new(new(2026, 10, 18), EntryKind.Expense, 1m, "Food")));
        Assert.Equal("ledger_entry_not_found", missing.GetProperty("errorCode").GetString());
        var invalidMonth = await host.Call("ledger.list", new LedgerMonthRequest(default));
        Assert.Equal("ledger_invalid_request", invalidMonth.GetProperty("errorCode").GetString());
        var invalidId = await host.Call("ledger.get", new LedgerEntryRequest(Guid.Empty));
        Assert.Equal("ledger_invalid_request", invalidId.GetProperty("errorCode").GetString());
        var csv = await host.Call("ledger.import-csv", new ImportLedgerCsvRequest(new string('账', 800000)));
        Assert.Equal("ledger_invalid_request", csv.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task NullPayloadIsRejectedByTheHostWithoutWriting()
    {
        await using var host = await Harness.Create();
        // NOF's generated dispatcher rejects null before entering the typed handler.
        var response = await host.Dispatch("ledger.create", null);
        Assert.NotNull(response.Failure);
        Assert.Equal("rpc_invocation_failed", response.Failure.ErrorCode);
        Assert.Empty((await host.Call("ledger.list", new LedgerMonthRequest(new(2026, 10, 1)))).GetProperty("value").EnumerateArray());
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutWriting()
    {
        await using var host = await Harness.Create();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Call("ledger.create",
            new SaveLedgerEntryRequest(new(2026, 10, 18), EntryKind.Expense, 10m, "Food"), cancellationToken: cancelled.Token));
        Assert.Empty((await host.Call("ledger.list", new LedgerMonthRequest(new(2026, 10, 1)))).GetProperty("value").EnumerateArray());
    }

    private static void AssertSuccess(JsonElement result) => Assert.True(result.GetProperty("isSuccess").GetBoolean(), result.ToString());

    private sealed class Harness(SqliteConnection connection, ServiceProvider provider, PluginRpcRuntime runtime,
        PluginRpcSerializer serializer, PluginRpcRegistry registry) : IAsyncDisposable
    {
        public static async Task<Harness> Create()
        {
            var connection = new SqliteConnection($"Data Source=ledger-rpc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await connection.OpenAsync();
            await using (var migration = connection.CreateCommand())
            {
                migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "001_init.sql"));
                migration.CommandText += await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "002_entry_relations.sql"));
                await migration.ExecuteNonQueryAsync();
            }
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
            => registry.InvokeAsync(method, JsonSerializer.SerializeToElement(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                new PluginRpcCallContext("quantum.plugin.test-caller", Guid.NewGuid().ToString("N"), new Dictionary<string, JsonElement>()), expectsValue, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await runtime.DisposeAsync();
            serializer.Dispose();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
