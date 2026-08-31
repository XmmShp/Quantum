using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.ExtensionMarket.Contract;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Application.Handlers;

public sealed class ListPlugins(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    IRepository<MarketUser> users) : ExtensionMarketService.ListPlugins
{
    public override async Task<Result<PluginSummary[]>> HandleAsync(
        ListPluginsRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        MarketUserId? authorId = null;
        if (!string.IsNullOrWhiteSpace(request.AuthorUserId))
        {
            if (!HandlerSupport.TryParseUserId(request.AuthorUserId, out var parsedAuthorId))
            {
                return Result.Fail("invalid_author_user_id", "AuthorUserId must be a positive 64-bit integer string.");
            }

            authorId = parsedAuthorId;
        }

        var search = request.Search?.Trim();
        if (search?.Length > 200)
        {
            return Result.Fail("invalid_plugin_search", "Search text cannot exceed 200 characters.");
        }

        var requestedTags = request.Tags
            .Select(static tag => tag.Trim().ToLowerInvariant())
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedTags.Length > 20 || requestedTags.Any(static tag => tag.Length > 50))
        {
            return Result.Fail("invalid_plugin_tags", "At most 20 tags of 50 characters are allowed.");
        }

        IQueryable<PluginListing> query = listings.AsNoTracking();
        if (authorId is { } filterAuthorId)
        {
            query = query.Where(candidate => candidate.AuthorUserId == filterAuthorId);
        }

        var candidates = await query.OrderBy(static listing => listing.Name)
            .ToArrayAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            candidates = candidates.Where(listing =>
                    listing.PluginId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    listing.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    listing.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (requestedTags.Length > 0)
        {
            candidates = candidates.Where(listing =>
                    requestedTags.All(tag => listing.Tags.Contains(tag, StringComparer.Ordinal)))
                .ToArray();
        }

        var listingIds = candidates.Select(static listing => listing.Id).ToArray();
        var published = listingIds.Length == 0
            ? []
            : await releases.AsNoTracking()
                .Where(release => listingIds.Contains(release.ListingId) &&
                    release.Status == PluginReleaseStatus.Published)
                .ToArrayAsync(cancellationToken);
        var visibleListings = candidates
            .Where(listing => published.Any(release => release.ListingId == listing.Id))
            .ToArray();
        var authorIds = visibleListings.Select(static listing => listing.AuthorUserId).Distinct().ToArray();
        var authors = authorIds.Length == 0
            ? []
            : await users.AsNoTracking()
                .Where(user => authorIds.Contains(user.Id))
                .ToArrayAsync(cancellationToken);

        return visibleListings.Select(listing => listing.ToSummary(
                authors.SingleOrDefault(author => author.Id == listing.AuthorUserId),
                published.Where(release => release.ListingId == listing.Id)
                    .OrderByDescending(static release => release.UploadedAtUtc)
                    .FirstOrDefault()))
            .ToArray();
    }
}

public sealed class GetPlugin(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    IRepository<MarketUser> users) : ExtensionMarketService.GetPlugin
{
    public override async Task<Result<PluginDetails>> HandleAsync(
        GetPluginRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        string pluginId;
        try
        {
            pluginId = PluginListing.NormalizePluginId(request.PluginId);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin_id", exception.Message);
        }

        var listing = await listings.AsNoTracking()
            .Where(candidate => candidate.PluginId == pluginId)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null)
        {
            return Result.Fail("plugin_not_found", "The plugin was not found.");
        }

        var published = await releases.AsNoTracking()
            .Where(release => release.ListingId == listing.Id &&
                release.Status == PluginReleaseStatus.Published)
            .OrderByDescending(static release => release.UploadedAtUtc)
            .ToArrayAsync(cancellationToken);
        if (published.Length == 0)
        {
            return Result.Fail("plugin_not_found", "The plugin was not found.");
        }

        var author = await users.AsNoTracking()
            .Where(candidate => candidate.Id == listing.AuthorUserId)
            .SingleOrDefaultAsync(cancellationToken);
        return new PluginDetails
        {
            Plugin = listing.ToSummary(author, published[0]),
            Releases = published.Select(release => release.ToSummary(listing.PluginId)).ToArray()
        };
    }
}

public sealed class CreatePlugin(
    IRepository<PluginListing> listings,
    MarketCallerResolver callerResolver,
    AuditWriter auditWriter,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    IDbContext dbContext) : ExtensionMarketService.CreatePlugin
{
    public override async Task<Result<PluginSummary>> HandleAsync(
        CreatePluginRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(
            MarketUserRole.Developer | MarketUserRole.Admin,
            cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        try
        {
            var pluginId = PluginListing.NormalizePluginId(request.PluginId);
            if (await listings.AsNoTracking().AnyAsync(
                    candidate => candidate.PluginId == pluginId,
                    cancellationToken))
            {
                return Result.Fail("plugin_conflict", "The plugin id is already registered.");
            }

            var listing = PluginListing.Create(
                pluginId,
                request.Name,
                request.Description,
                user.Id,
                request.Tags,
                idGenerator,
                timeProvider);
            await listings.AddAsync(listing, cancellationToken);
            await auditWriter.WriteAsync(
                "plugin.created",
                user.Id,
                $"Plugin '{listing.PluginId}' was created.",
                listing.Id,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return listing.ToSummary(user, null);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin", exception.Message);
        }
    }
}

public sealed class UpdatePlugin(
    IRepository<PluginListing> listings,
    MarketCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : ExtensionMarketService.UpdatePlugin
{
    public override async Task<Result<PluginSummary>> HandleAsync(
        UpdatePluginRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Developer | MarketUserRole.Admin, cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        string pluginId;
        try
        {
            pluginId = PluginListing.NormalizePluginId(request.PluginId);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin_id", exception.Message);
        }

        var listing = await listings.Where(candidate => candidate.PluginId == pluginId)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null || !HandlerSupport.CanManage(listing, user))
        {
            return Result.Fail("plugin_not_found", "The plugin was not found.");
        }

        try
        {
            listing.Update(request.Name, request.Description, request.Tags);
            await auditWriter.WriteAsync(
                "plugin.updated",
                user.Id,
                $"Plugin '{listing.PluginId}' was updated.",
                listing.Id,
                null,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return listing.ToSummary(user.Id == listing.AuthorUserId ? user : null, null);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin", exception.Message);
        }
    }
}

public sealed class DeletePlugin(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    MarketCallerResolver callerResolver,
    IPluginPackageStore packageStore,
    AuditWriter auditWriter,
    IDbContext dbContext) : ExtensionMarketService.DeletePlugin
{
    public override async Task<Result> HandleAsync(
        DeletePluginRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Developer | MarketUserRole.Admin, cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        string pluginId;
        try
        {
            pluginId = PluginListing.NormalizePluginId(request.PluginId);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin_id", exception.Message);
        }

        var listing = await listings.Where(candidate => candidate.PluginId == pluginId)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null || !HandlerSupport.CanManage(listing, user))
        {
            return Result.Fail("plugin_not_found", "The plugin was not found.");
        }

        var packages = await releases
            .Where(release => release.ListingId == listing.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var release in packages)
        {
            releases.Remove(release);
        }

        listings.Remove(listing);
        await auditWriter.WriteAsync(
            "plugin.deleted",
            user.Id,
            $"Plugin '{listing.PluginId}' and {packages.Length} release(s) were deleted.",
            null,
            null,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var release in packages)
        {
            await packageStore.DeleteAsync(release.PackagePath, cancellationToken);
        }

        return Result.Success();
    }
}
