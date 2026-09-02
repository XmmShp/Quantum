using System.Security.Claims;
using Quantum.Platform.Application;

namespace Quantum.Platform.Authentication;

public sealed class HttpPlatformCallerContext(IHttpContextAccessor httpContextAccessor) : IPlatformCallerContext
{
    public string? UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
