using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application.Handlers;

public sealed class CreateCredentialClient(
    IRepository<PluginListing> listings,
    PlatformCallerResolver callerResolver,
    ICredentialClientProtocolRepository clients) : QuantumPlatformService.CreateCredentialClient
{
    public override async Task<Result<CreateCredentialClientResponse>> HandleAsync(
        CreateCredentialClientRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(
            PlatformUserRole.Developer | PlatformUserRole.Admin,
            cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        string clientId;
        string displayName;
        try
        {
            clientId = NormalizeClientId(request.ClientId);
            displayName = NormalizeDisplayName(request.DisplayName);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_credential_client", exception.Message);
        }

        var pluginIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var value in request.AllowedPluginIds)
            {
                pluginIds.Add(PluginListing.NormalizePluginId(value));
            }
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin_id", exception.Message);
        }

        if (pluginIds.Count == 0)
        {
            return Result.Fail("credential_client_plugins_required", "At least one allowed plugin is required.");
        }

        var ownedPluginIds = await listings.AsNoTracking()
            .Where(listing => pluginIds.Contains(listing.PluginId) &&
                (listing.AuthorUserId == user.Id || user.Roles.HasFlag(PlatformUserRole.Admin)))
            .Select(static listing => listing.PluginId)
            .ToArrayAsync(cancellationToken);
        if (ownedPluginIds.Length != pluginIds.Count)
        {
            return Result.Fail("credential_client_plugin_forbidden", "Every allowed plugin must be managed by the current user.");
        }

        var keySet = CredentialClientKeyPairGenerator.Generate();
        var create = await clients.CreateAsync(
            new CreateCredentialClientProtocolRequest(
                clientId,
                displayName,
                ContractMapping.Format(user.Id),
                pluginIds.Order(StringComparer.Ordinal).ToArray(),
                keySet.JsonWebKeySet),
            cancellationToken);
        if (!create.IsSuccess)
        {
            return Result<CreateCredentialClientResponse>.From(create);
        }

        return new CreateCredentialClientResponse
        {
            Client = Map(create.Value),
            GeneratedKeySet = keySet
        };
    }

    internal static CredentialClientSummary Map(CredentialClientProtocolDescriptor client) => new()
    {
        ClientId = client.ClientId,
        DisplayName = client.DisplayName,
        AllowedPluginIds = client.AllowedPluginIds.ToArray(),
        IsEnabled = client.IsEnabled,
        CreatedAtUtc = client.CreatedAtUtc,
        UpdatedAtUtc = client.UpdatedAtUtc
    };

    private static string NormalizeClientId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 128 || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("ClientId must contain 3 to 128 ASCII letters, digits, dots, hyphens, or underscores.");
        }

        return normalized;
    }

    private static string NormalizeDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 200 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("DisplayName must contain at most 200 visible characters.");
        }

        return normalized;
    }
}

public sealed class ListCredentialClients(
    PlatformCallerResolver callerResolver,
    ICredentialClientProtocolRepository clients) : QuantumPlatformService.ListCredentialClients
{
    public override async Task<Result<CredentialClientSummary[]>> HandleAsync(
        EmptyRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.Developer | PlatformUserRole.Admin, cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        var ownerId = ContractMapping.Format(user.Id);
        return (await clients.ListAsync(cancellationToken))
            .Where(client => client.OwnerUserId == ownerId)
            .Select(CreateCredentialClient.Map)
            .ToArray();
    }
}

public sealed class DeleteCredentialClient(
    PlatformCallerResolver callerResolver,
    ICredentialClientProtocolRepository clients) : QuantumPlatformService.DeleteCredentialClient
{
    public override async Task<Result> HandleAsync(
        DeleteCredentialClientRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.Developer | PlatformUserRole.Admin, cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        var ownerId = ContractMapping.Format(user.Id);
        var client = (await clients.ListAsync(cancellationToken)).SingleOrDefault(candidate =>
            candidate.ClientId == request.ClientId.Trim() && candidate.OwnerUserId == ownerId);
        return client is null
            ? Result.Fail("credential_client_not_found", "The Credential Client was not found.")
            : await clients.DeleteAsync(client.ClientId, cancellationToken);
    }
}
