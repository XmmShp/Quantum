using System.Security.Claims;
using NOF.Contract;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using Quantum.Platform.Application;

namespace Quantum.Platform.Authentication;

internal sealed class NofCredentialClientProtocolRepository(IOAuthClientRepository clients)
    : ICredentialClientProtocolRepository
{
    private const string OwnerClaim = "quantum_owner_user_id";

    public async Task<IReadOnlyList<CredentialClientProtocolDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => (await clients.ListAsync(cancellationToken))
            .Where(client => client.AccessTokenClaims.Any(claim => claim.Type == OwnerClaim))
            .Select(Map)
            .ToArray();

    public async Task<Result<CredentialClientProtocolDescriptor>> CreateAsync(
        CreateCredentialClientProtocolRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await clients.CreateAsync(new CreateOAuthClientRequest
        {
            ClientId = request.ClientId,
            DisplayName = request.DisplayName,
            AllowedScopes = [QuantumPlatformPermissions.PluginPublish],
            AccessTokenClaims =
            [
                new OAuthClientClaim(ClaimTypes.NameIdentifier, request.OwnerUserId),
                new OAuthClientClaim(ClaimTypes.Permission, QuantumPlatformPermissions.PluginPublish),
                new OAuthClientClaim(OwnerClaim, request.OwnerUserId),
                .. request.AllowedPluginIds.Select(pluginId =>
                    new OAuthClientClaim(ClaimTypes.Permission, $"{QuantumPlatformPermissions.PluginPrefix}{pluginId}"))
            ],
            JsonWebKeySet = request.JsonWebKeySet,
            TokenEndpointAuthenticationMethod = OAuthClientAuthenticationMethods.PrivateKeyJwt,
            AllowedGrantTypes = [OAuthGrantTypes.ClientCredentials],
            ClientType = OAuthClientType.Confidential,
            IsEnabled = true
        }, cancellationToken);
        return result.IsSuccess ? Map(result.Value.Client) : Result<CredentialClientProtocolDescriptor>.From(result);
    }

    public Task<Result> DeleteAsync(string clientId, CancellationToken cancellationToken = default)
        => clients.DeleteAsync(clientId, cancellationToken);

    private static CredentialClientProtocolDescriptor Map(OAuthClientDescriptor client)
    {
        var owner = client.AccessTokenClaims.Single(claim => claim.Type == OwnerClaim).Value;
        var plugins = client.AccessTokenClaims
            .Where(claim => claim.Type == ClaimTypes.Permission &&
                claim.Value.StartsWith(QuantumPlatformPermissions.PluginPrefix, StringComparison.Ordinal) &&
                claim.Value != QuantumPlatformPermissions.PluginPublish)
            .Select(claim => claim.Value[QuantumPlatformPermissions.PluginPrefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new(client.ClientId, client.DisplayName, owner, plugins, client.IsEnabled, client.CreatedAtUtc, client.UpdatedAtUtc);
    }
}
