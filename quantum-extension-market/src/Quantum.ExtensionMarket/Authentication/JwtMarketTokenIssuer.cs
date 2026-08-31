using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quantum.ExtensionMarket.Application;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Authentication;

public sealed class JwtMarketTokenIssuer(
    IOptions<MarketJwtOptions> options,
    TimeProvider timeProvider) : IMarketTokenIssuer
{
    private readonly MarketJwtOptions options = options.Value;

    public IssuedAccessToken Issue(MarketUser user)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.LifetimeMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, ((long)user.Id).ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        foreach (var role in Enum.GetValues<MarketUserRole>())
        {
            if (role != MarketUserRole.None && user.HasAnyRole(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }
        }

        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt.UtcDateTime);
    }
}
