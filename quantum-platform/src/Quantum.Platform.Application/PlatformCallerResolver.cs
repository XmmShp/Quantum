using System.Globalization;
using NOF.Domain;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public sealed class PlatformCallerResolver(
    IPlatformCallerContext callerContext,
    IRepository<PlatformUser> users)
{
    public async Task<PlatformCallerResolution> RequireAsync(
        PlatformUserRole requiredRoles,
        CancellationToken cancellationToken)
    {
        if (TryParseId(callerContext.UserId) is not { } userId)
        {
            return PlatformCallerResolution.Fail("authentication_required", "A valid bearer token is required.");
        }

        var user = await users
            .Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return PlatformCallerResolution.Fail("user_not_found", "The authenticated user no longer exists.");
        }

        if (requiredRoles != PlatformUserRole.None && !user.HasAnyRole(requiredRoles))
        {
            return PlatformCallerResolution.Fail("forbidden", "The authenticated user does not have the required role.");
        }

        return PlatformCallerResolution.Success(user);
    }

    public async Task<PlatformUser?> ResolveOptionalAsync(CancellationToken cancellationToken)
    {
        if (TryParseId(callerContext.UserId) is not { } userId)
        {
            return null;
        }

        return await users
            .Where(candidate => candidate.Id == userId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static PlatformUserId? TryParseId(string? value)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return PlatformUserId.Of(parsed);
        }

        return null;
    }
}

public sealed record PlatformCallerResolution(
    PlatformUser? User,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PlatformCallerResolution Success(PlatformUser user) => new(user, null, null);

    public static PlatformCallerResolution Fail(string code, string message) => new(null, code, message);
}
