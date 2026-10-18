using NOF.Contract;

namespace Quantum.OfficialPlugins;

/// <summary>UI boundary helper; failed RPC calls must not be treated as successful saves.</summary>
internal static class RpcResults
{
    public static T ValueOrThrow<T>(Result<T> result)
    {
        ThrowIfFailed(result);
        return result.Value!;
    }

    public static void ThrowIfFailed(IResult result)
    {
        if (!result.IsSuccess)
        {
            throw new RpcFailureException(result.Message ?? result.ErrorCode ?? "RPC failed.");
        }
    }
}
