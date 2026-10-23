using System.Text;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Hosting;
using Quantum.Platform.Application;

namespace Quantum.Platform.Authentication;

public static class AuthenticationExtensions
{
    private const string SelectorScheme = "QuantumBearer";
    private const string UserScheme = "QuantumUserBearer";
    private const string CredentialScheme = "QuantumCredentialBearer";

    public static IHostApplicationBuilder AddQuantumPlatformAuthentication(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(PlatformJwtOptions.SectionName).Get<PlatformJwtOptions>() ?? new();
        options.Validate();
        var oidc = builder.Configuration.GetSection(QuantumPlatformOidcOptions.SectionName)
            .Get<QuantumPlatformOidcOptions>() ?? new();
        oidc.Validate(builder.Environment.IsDevelopment());

        if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(oidc.SigningKeyEncryptionKey))
        {
            throw new InvalidOperationException("QuantumPlatform:OidcServer:SigningKeyEncryptionKey is required outside Development.");
        }

        builder.AddOidcServer(server =>
        {
            server.Issuer = oidc.Issuer.TrimEnd('/');
            server.PathBase = oidc.PathBase;
            server.AccessTokenAudience = oidc.Audience;
            server.SigningKeyEncryptionKey = oidc.SigningKeyEncryptionKey;
            server.ScopesSupported = [QuantumPlatformPermissions.PluginPublish];
            server.AccessTokenExpiration = TimeSpan.FromMinutes(10);
        });
        builder.Services.AddScoped<ICredentialClientProtocolRepository, NofCredentialClientProtocolRepository>();
        builder.Services.AddScoped<IOAuthSubjectService, QuantumOAuthSubjectService>();
        builder.Services.Configure<QuantumPlatformOidcOptions>(
            builder.Configuration.GetSection(QuantumPlatformOidcOptions.SectionName));
        builder.Services.Configure<PlatformJwtOptions>(
            builder.Configuration.GetSection(PlatformJwtOptions.SectionName));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IPlatformCallerContext, HttpPlatformCallerContext>();
        builder.Services.AddSingleton<IPlatformTokenIssuer, JwtPlatformTokenIssuer>();
        builder.Services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, SelectorScheme, policy =>
            {
                policy.ForwardDefaultSelector = context => SelectScheme(context, oidc.Issuer);
            })
            .AddJwtBearer(UserScheme, jwt =>
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
            })
            .AddJwtBearer(CredentialScheme, jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.Authority = oidc.Issuer.TrimEnd('/');
                jwt.MetadataAddress = $"{oidc.Issuer.TrimEnd('/')}/.well-known/openid-configuration";
                jwt.Audience = oidc.Audience;
                jwt.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = oidc.Issuer.TrimEnd('/'),
                    ValidateAudience = true,
                    ValidAudience = oidc.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
            });
        builder.Services.AddAuthorization();
        return builder;
    }

    private static string SelectScheme(HttpContext context, string credentialIssuer)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return UserScheme;
        }

        try
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(authorization[7..].Trim());
            return string.Equals(token.Issuer, credentialIssuer.TrimEnd('/'), StringComparison.Ordinal)
                ? CredentialScheme
                : UserScheme;
        }
        catch
        {
            return UserScheme;
        }
    }
}
