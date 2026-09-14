using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;
using Quantum.OfficialPlugins.Calendar.Domain;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed record ListCalendarEntriesRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    CalendarEntryKind? Kind = null);

public sealed record ListCalendarTasksRequest(bool IncludeCompleted = true);

public sealed record CalendarEntryRequest(Guid Id);

public sealed record CalendarEntryLookup(CalendarEntryDetails? Entry);

public sealed record UpdateCalendarEntryRequest(Guid Id, SaveCalendarEntryRequest Entry);

public sealed record SetCalendarEntryCompletedRequest(Guid Id, bool IsCompleted);

[TransportOverQuantum]
[RpcInvocationName("calendar")]
public interface ICalendarRpcService : IRpcService
{
    [RpcInvocationName("list")]
    Result<CalendarEntryDetails[]> ListEntries(ListCalendarEntriesRequest request);

    [RpcInvocationName("list-tasks")]
    Result<CalendarEntryDetails[]> ListTasks(ListCalendarTasksRequest request);

    [RpcInvocationName("get")]
    Result<CalendarEntryLookup> GetEntry(CalendarEntryRequest request);

    [RpcInvocationName("create")]
    Result<CalendarEntryDetails> CreateEntry(SaveCalendarEntryRequest request);

    [RpcInvocationName("update")]
    Result<CalendarEntryDetails> UpdateEntry(UpdateCalendarEntryRequest request);

    [RpcInvocationName("set-completed")]
    Result<CalendarEntryDetails> SetCompleted(SetCalendarEntryCompletedRequest request);

    [RpcInvocationName("delete")]
    Result DeleteEntry(CalendarEntryRequest request);
}

public partial class CalendarRpcService : RpcServer<ICalendarRpcService>;
