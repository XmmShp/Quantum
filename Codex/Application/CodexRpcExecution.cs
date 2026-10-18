using NOF.Contract;

namespace Quantum.OfficialPlugins.Codex.Application;

internal static class CodexRpcExecution
{
    public static async Task<Result<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        catch (CodexConnectionException ex)
        {
            return Result.Fail("codex_connection_failed", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result.Fail("codex_invalid_request", ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return Result.Fail("codex_invalid_request", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("codex_invalid_state", ex.Message);
        }
    }
}
