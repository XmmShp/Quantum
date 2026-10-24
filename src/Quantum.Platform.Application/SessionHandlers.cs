using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application.Handlers;

public sealed class RefreshLogin(
    IRepository<PlatformLoginSession> sessions, IRepository<PlatformUser> users,
    IRegistrationLock sessionLock, IRefreshTokenCodec tokens, IPlatformTokenIssuer issuer,
    TimeProvider clock, IDbContext db) : QuantumPlatformService.RefreshLogin
{
    public override async Task<Result<LoginResponse>> HandleAsync(RefreshLoginRequest request, Context context, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || !SessionToken.TryGetId(request.RefreshToken, out var id)) return Invalid();
        await using var lease = await sessionLock.AcquireAsync("login-session:" + id, cancellationToken);
        var session = await sessions.Where(s => s.Id == id).SingleOrDefaultAsync(cancellationToken);
        if (session is null) return Invalid();
        var user = await users.Where(u => u.Id == session.UserId).SingleOrDefaultAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (user is null || !session.IsActive(user, now)) return Invalid();
        var disposition = session.Inspect(request.RefreshToken, request.RequestId, now);
        if (disposition == RefreshDisposition.Replay)
        {
            session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            return Invalid();
        }
        if (disposition == RefreshDisposition.Invalid) return Invalid();
        var next = tokens.Rotate(request.RefreshToken, request.RequestId);
        if (disposition == RefreshDisposition.Current)
        {
            session.Rotate(request.RefreshToken, next, request.RequestId, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        var access = issuer.Issue(user, session.Id);
        return new LoginResponse
        {
            AccessToken = access.AccessToken,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = next,
            RefreshExpiresAtUtc = session.ExpiresAtUtc,
            User = user.ToSummary()
        };
    }

    private static Result<LoginResponse> Invalid() => Result.Fail("invalid_refresh_token", "The login session has expired or was revoked. Sign in again.");
}

public sealed class Logout(IRepository<PlatformLoginSession> sessions, IRegistrationLock sessionLock,
    TimeProvider clock, IDbContext db) : QuantumPlatformService.Logout
{
    public override async Task<Result> HandleAsync(LogoutRequest request, Context context, CancellationToken cancellationToken)
    {
        if (!SessionToken.TryGetId(request.RefreshToken, out var id)) return Result.Success();
        await using var lease = await sessionLock.AcquireAsync("login-session:" + id, cancellationToken);
        var session = await sessions.Where(s => s.Id == id).SingleOrDefaultAsync(cancellationToken);
        if (session is not null && session.Inspect(request.RefreshToken, Guid.Empty, clock.GetUtcNow().UtcDateTime) != RefreshDisposition.Invalid)
        {
            session.Revoke(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }
}

internal static class SessionToken
{
    public static bool TryGetId(string? token, out Guid id)
    {
        id = default;
        return token is { Length: 97 } && token[32] == '.' && Guid.TryParseExact(token[..32], "N", out id) &&
            token.AsSpan(33).ContainsAnyExcept("0123456789abcdef") == false;
    }
}
