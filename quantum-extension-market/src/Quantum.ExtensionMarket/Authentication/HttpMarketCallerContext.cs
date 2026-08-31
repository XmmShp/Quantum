using System.Security.Claims;
using Quantum.ExtensionMarket.Application;

namespace Quantum.ExtensionMarket.Authentication;

public sealed class HttpMarketCallerContext(IHttpContextAccessor httpContextAccessor) : IMarketCallerContext
{
    public string? UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
