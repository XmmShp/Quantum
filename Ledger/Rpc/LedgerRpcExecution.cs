using NOF.Contract;

namespace Quantum.OfficialPlugins.Ledger.Rpc;

internal static class LedgerRpcExecution
{
    public static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Invalid record ID.");
        }
    }

    public static async Task<Result<T>> RunAsync<T>(object? request, Func<Task<T>> action)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return await action();
        }
        catch (KeyNotFoundException ex)
        {
            return Result.Fail("ledger_entry_not_found", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result.Fail("ledger_invalid_request", ex.Message);
        }
    }

    public static async Task<Result> RunAsync(object? request, Func<Task> action)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            await action();
            return Result.Success();
        }
        catch (KeyNotFoundException ex)
        {
            return Result.Fail("ledger_entry_not_found", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result.Fail("ledger_invalid_request", ex.Message);
        }
    }
}
