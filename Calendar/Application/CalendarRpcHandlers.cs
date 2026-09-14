using NOF.Contract;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class ListCalendarEntries(ICalendarApplicationService calendar)
    : CalendarRpcService.ListEntries
{
    public override async Task<Result<CalendarEntryDetails[]>> HandleAsync(
        ListCalendarEntriesRequest request,
        Context context,
        CancellationToken cancellationToken)
        => await CalendarRpcExecution.RunAsync(
            async () => (await calendar.ListAsync(
                request.StartDate,
                request.EndDate,
                request.Kind,
                cancellationToken)).ToArray());
}

public sealed class ListCalendarTasks(ICalendarApplicationService calendar)
    : CalendarRpcService.ListTasks
{
    public override async Task<Result<CalendarEntryDetails[]>> HandleAsync(
        ListCalendarTasksRequest request,
        Context context,
        CancellationToken cancellationToken)
        => await CalendarRpcExecution.RunAsync(
            async () => (await calendar.ListTasksAsync(
                request.IncludeCompleted,
                cancellationToken)).ToArray());
}

public sealed class GetCalendarEntry(ICalendarApplicationService calendar)
    : CalendarRpcService.GetEntry
{
    public override Task<Result<CalendarEntryLookup>> HandleAsync(
        CalendarEntryRequest request,
        Context context,
        CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(async () =>
            new CalendarEntryLookup(await calendar.GetAsync(request.Id, cancellationToken)));
}

public sealed class CreateCalendarEntry(ICalendarApplicationService calendar)
    : CalendarRpcService.CreateEntry
{
    public override Task<Result<CalendarEntryDetails>> HandleAsync(
        SaveCalendarEntryRequest request,
        Context context,
        CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(() => calendar.CreateAsync(request, cancellationToken));
}

public sealed class UpdateCalendarEntry(ICalendarApplicationService calendar)
    : CalendarRpcService.UpdateEntry
{
    public override Task<Result<CalendarEntryDetails>> HandleAsync(
        UpdateCalendarEntryRequest request,
        Context context,
        CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(() =>
            calendar.UpdateAsync(request.Id, request.Entry, cancellationToken));
}

public sealed class SetCalendarEntryCompleted(ICalendarApplicationService calendar)
    : CalendarRpcService.SetCompleted
{
    public override Task<Result<CalendarEntryDetails>> HandleAsync(
        SetCalendarEntryCompletedRequest request,
        Context context,
        CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(() =>
            calendar.SetCompletedAsync(request.Id, request.IsCompleted, cancellationToken));
}

public sealed class DeleteCalendarEntry(ICalendarApplicationService calendar)
    : CalendarRpcService.DeleteEntry
{
    public override Task<Result> HandleAsync(
        CalendarEntryRequest request,
        Context context,
        CancellationToken cancellationToken)
        => CalendarRpcExecution.RunAsync(() => calendar.DeleteAsync(request.Id, cancellationToken));
}

internal static class CalendarRpcExecution
{
    public static async Task<Result<T>> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException exception)
        {
            return Result.Fail("calendar_entry_not_found", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("calendar_invalid_request", exception.Message);
        }
    }

    public static async Task<Result> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return Result.Success();
        }
        catch (KeyNotFoundException exception)
        {
            return Result.Fail("calendar_entry_not_found", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("calendar_invalid_request", exception.Message);
        }
    }
}
