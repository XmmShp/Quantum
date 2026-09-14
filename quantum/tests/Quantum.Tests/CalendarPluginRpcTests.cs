using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NOF.Contract;
using System.Text.Json;
using Quantum.OfficialPlugins.Calendar.Application;
using Quantum.OfficialPlugins.Calendar.Domain;
using Quantum.OfficialPlugins.Calendar.Hosting;
using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class CalendarPluginRpcTests
{
    [Fact]
    public void CalendarRpcContractUsesStableServiceAndMethodNames()
    {
        var serviceName = Assert.Single(
            typeof(ICalendarRpcService).GetCustomAttributes(typeof(RpcInvocationNameAttribute), false));
        Assert.Equal("calendar", Assert.IsType<RpcInvocationNameAttribute>(serviceName).Name);

        var methodNames = typeof(ICalendarRpcService)
            .GetMethods()
            .ToDictionary(
                static method => method.Name,
                static method => Assert.IsType<RpcInvocationNameAttribute>(Assert.Single(
                    method.GetCustomAttributes(typeof(RpcInvocationNameAttribute), false))).Name);

        Assert.Equal("list", methodNames[nameof(ICalendarRpcService.ListEntries)]);
        Assert.Equal("list-tasks", methodNames[nameof(ICalendarRpcService.ListTasks)]);
        Assert.Equal("get", methodNames[nameof(ICalendarRpcService.GetEntry)]);
        Assert.Equal("create", methodNames[nameof(ICalendarRpcService.CreateEntry)]);
        Assert.Equal("update", methodNames[nameof(ICalendarRpcService.UpdateEntry)]);
        Assert.Equal("set-completed", methodNames[nameof(ICalendarRpcService.SetCompleted)]);
        Assert.Equal("delete", methodNames[nameof(ICalendarRpcService.DeleteEntry)]);
    }

    [Fact]
    public void CalendarRpcPayloadUsesWebJsonAndStringKinds()
    {
        var payload = JsonSerializer.Serialize(
            new SaveCalendarEntryRequest(
                "RPC",
                string.Empty,
                CalendarEntryKind.Task,
                new DateOnly(2026, 9, 15),
                new TimeOnly(9, 30),
                null,
                "violet"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"kind\":\"Task\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"date\":\"2026-09-15\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"startTime\":\"09:30:00\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CalendarRpcHandlersAreRegisteredAndDelegateToApplicationService()
    {
        var expected = new CalendarEntryDetails(
            Guid.NewGuid(),
            "联动事项",
            string.Empty,
            CalendarEntryKind.Task,
            new DateOnly(2026, 9, 15),
            new TimeOnly(9, 0),
            null,
            false,
            "violet");
        var services = new ServiceCollection();
        CalendarPluginInitializer.Initialize(services);
        services.AddSingleton<ICalendarApplicationService>(new StubCalendarService(expected));
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ListCalendarEntries>(provider.GetRequiredService<CalendarRpcService.ListEntries>());
        Assert.IsType<ListCalendarTasks>(provider.GetRequiredService<CalendarRpcService.ListTasks>());
        Assert.IsType<GetCalendarEntry>(provider.GetRequiredService<CalendarRpcService.GetEntry>());
        Assert.IsType<UpdateCalendarEntry>(provider.GetRequiredService<CalendarRpcService.UpdateEntry>());
        Assert.IsType<SetCalendarEntryCompleted>(provider.GetRequiredService<CalendarRpcService.SetCompleted>());
        Assert.IsType<DeleteCalendarEntry>(provider.GetRequiredService<CalendarRpcService.DeleteEntry>());

        var handler = provider.GetRequiredService<CalendarRpcService.CreateEntry>();
        var result = await handler.HandleAsync(
            new SaveCalendarEntryRequest(
                expected.Title,
                expected.Notes,
                expected.Kind,
                expected.Date,
                expected.StartTime,
                expected.EndTime,
                expected.Style),
            Context.Empty,
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"{result.ErrorCode}: {result.Message}");
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task CalendarRpcMapsDomainValidationToStableFailure()
    {
        var services = new ServiceCollection();
        CalendarPluginInitializer.Initialize(services);
        services.AddSingleton<ICalendarApplicationService>(new StubCalendarService(null));
        await using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<CalendarRpcService.CreateEntry>();

        var result = await handler.HandleAsync(
            new SaveCalendarEntryRequest(
                string.Empty,
                string.Empty,
                CalendarEntryKind.Task,
                new DateOnly(2026, 9, 15),
                new TimeOnly(9, 0),
                null,
                "violet"),
            Context.Empty,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("calendar_invalid_request", result.ErrorCode);
    }

    [Fact]
    public async Task CalendarRpcCanBeInvokedThroughTheQuantumNameRouter()
    {
        var expected = new CalendarEntryDetails(
            Guid.NewGuid(),
            "跨插件待办",
            string.Empty,
            CalendarEntryKind.Task,
            new DateOnly(2026, 9, 15),
            new TimeOnly(9, 0),
            null,
            false,
            "blue");
        var services = new ServiceCollection();
        CalendarPluginInitializer.Initialize(services);
        services.AddSingleton<ICalendarApplicationService>(new StubCalendarService(expected));
        await using var provider = services.BuildServiceProvider();
        using var targetSerializer = new PluginRpcSerializer();
        await using var runtime = PluginRpcRuntime.Create(
            PluginId.Of("quantum.plugin.calendar"),
            Guid.NewGuid(),
            typeof(CalendarRpcService).Assembly,
            provider.GetRequiredService<IServiceScopeFactory>(),
            targetSerializer,
            NullLogger.Instance);
        runtime.Resume();
        var registry = PluginRpcRegistry.Create([runtime], NullLogger.Instance);
        using var callerSerializer = new PluginRpcSerializer();
        using var invoker = new PluginRpcInvoker(
            PluginId.Of("quantum.plugin.test-caller"),
            Guid.NewGuid(),
            callerSerializer);
        invoker.UseRegistry(registry);

        var result = await invoker.InvokeAsync<CalendarEntryDetails[]>(
            "quantum.plugin.calendar.calendar.list-tasks",
            new ListCalendarTasksRequest(IncludeCompleted: false),
            Context.Empty);

        Assert.True(result.IsSuccess, $"{result.ErrorCode}: {result.Message}");
        Assert.Equal([expected], result.Value);
    }

    private sealed class StubCalendarService(CalendarEntryDetails? entry) : ICalendarApplicationService
    {
        public Task<IReadOnlyList<CalendarEntryDetails>> ListAsync(
            DateOnly startDate,
            DateOnly endDate,
            CalendarEntryKind? kind = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CalendarEntryDetails>>(entry is null ? [] : [entry]);

        public Task<IReadOnlyList<CalendarEntryDetails>> ListTasksAsync(
            bool includeCompleted = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CalendarEntryDetails>>(entry is null ? [] : [entry]);

        public Task<CalendarEntryDetails?> GetAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(entry);

        public Task<CalendarEntryDetails> CreateAsync(
            SaveCalendarEntryRequest request,
            CancellationToken cancellationToken = default)
            => string.IsNullOrWhiteSpace(request.Title)
                ? Task.FromException<CalendarEntryDetails>(new ArgumentException("Title is required."))
                : Task.FromResult(entry!);

        public Task<CalendarEntryDetails> UpdateAsync(
            Guid id,
            SaveCalendarEntryRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(entry!);

        public Task<CalendarEntryDetails> SetCompletedAsync(
            Guid id,
            bool isCompleted,
            CancellationToken cancellationToken = default)
            => Task.FromResult(entry!);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
