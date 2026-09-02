using System.Globalization;
using NOF.Domain;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Application;

public sealed class MarketCallerResolver(
    IMarketCallerContext callerContext,
    IRepository<MarketUser> users)
{
    public async Task<MarketCallerResolution> RequireAsync(
        MarketUserRole requiredRoles,
        CancellationToken cancellationToken)
    {
        if (TryParseId(callerContext.UserId) is not { } userId)
        {
            return MarketCallerResolution.Fail("authentication_required", "A valid bearer token is required.");
        }

        var user = await users
            .Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return MarketCallerResolution.Fail("user_not_found", "The authenticated user no longer exists.");
        }

        if (requiredRoles != MarketUserRole.None && !user.HasAnyRole(requiredRoles))
        {
            return MarketCallerResolution.Fail("forbidden", "The authenticated user does not have the required role.");
        }

        return MarketCallerResolution.Success(user);
    }

    public async Task<MarketUser?> ResolveOptionalAsync(CancellationToken cancellationToken)
    {
        if (TryParseId(callerContext.UserId) is not { } userId)
        {
            return null;
        }

        return await users
            .Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static MarketUserId? TryParseId(string? value)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return MarketUserId.Of(parsed);
        }

        return null;
    }
}

public sealed record MarketCallerResolution(
    MarketUser? User,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static MarketCallerResolution Success(MarketUser user) => new(user, null, null);

    public static MarketCallerResolution Fail(string code, string message) => new(null, code, message);
}
