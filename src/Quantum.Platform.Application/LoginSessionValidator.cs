using NOF.Domain;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public sealed class LoginSessionValidator(IRepository<PlatformLoginSession> sessions, TimeProvider clock) : ILoginSessionValidator
{
    public async Task<bool> IsActiveAsync(PlatformUser user, string sessionId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(sessionId, "N", out var id)) return false;
        var session = await sessions.Where(s => s.Id == id).SingleOrDefaultAsync(cancellationToken);
        return session?.IsActive(user, clock.GetUtcNow().UtcDateTime) == true;
    }
}
