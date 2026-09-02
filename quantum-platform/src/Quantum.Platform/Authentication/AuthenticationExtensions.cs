using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Quantum.Platform.Application;

namespace Quantum.Platform.Authentication;

public static class AuthenticationExtensions
{
    public static IHostApplicationBuilder AddQuantumPlatformAuthentication(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(PlatformJwtOptions.SectionName).Get<PlatformJwtOptions>() ?? new();
        options.Validate();
        builder.Services.Configure<PlatformJwtOptions>(
            builder.Configuration.GetSection(PlatformJwtOptions.SectionName));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IPlatformCallerContext, HttpPlatformCallerContext>();
        builder.Services.AddSingleton<IPlatformTokenIssuer, JwtPlatformTokenIssuer>();
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
