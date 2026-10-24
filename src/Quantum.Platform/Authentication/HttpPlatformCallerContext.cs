using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Quantum.Platform.Application;

namespace Quantum.Platform.Authentication;

public sealed class HttpPlatformCallerContext(IHttpContextAccessor httpContextAccessor) : IPlatformCallerContext
{
    public string? LoginSessionId => httpContextAccessor.HttpContext?.User.FindFirstValue("sid");
    public string? UserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? CredentialClientId => httpContextAccessor.HttpContext?.User.FindFirstValue("client_id");

    public bool HasPermission(string permission)
        => httpContextAccessor.HttpContext?.User.FindAll(ClaimTypes.Permission)
            .Any(claim => string.Equals(claim.Value, permission, StringComparison.Ordinal)) == true;
}
