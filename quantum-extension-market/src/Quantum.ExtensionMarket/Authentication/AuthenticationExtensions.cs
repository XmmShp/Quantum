using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Quantum.ExtensionMarket.Application;

namespace Quantum.ExtensionMarket.Authentication;

public static class AuthenticationExtensions
{
    public static IHostApplicationBuilder AddExtensionMarketAuthentication(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(MarketJwtOptions.SectionName).Get<MarketJwtOptions>() ?? new();
        options.Validate();
        builder.Services.Configure<MarketJwtOptions>(
            builder.Configuration.GetSection(MarketJwtOptions.SectionName));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IMarketCallerContext, HttpMarketCallerContext>();
        builder.Services.AddSingleton<IMarketTokenIssuer, JwtMarketTokenIssuer>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
            });
        builder.Services.AddAuthorization();
        return builder;
    }
}
