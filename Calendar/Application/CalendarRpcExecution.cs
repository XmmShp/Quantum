using NOF.Contract;

namespace Quantum.OfficialPlugins.Calendar.Application;

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
