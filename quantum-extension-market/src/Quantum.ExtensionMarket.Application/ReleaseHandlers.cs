using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.ExtensionMarket.Contract;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Application.Handlers;

public sealed class ListPluginReleases(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    MarketCallerResolver callerResolver) : ExtensionMarketService.ListPluginReleases
{
    public override async Task<Result<PluginReleaseSummary[]>> HandleAsync(
        ListPluginReleasesRequest request,
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

        var caller = await callerResolver.ResolveOptionalAsync(cancellationToken);
        var canSeeModerationState = caller is not null &&
            (HandlerSupport.CanManage(listing, caller) || HandlerSupport.CanReview(caller));
        IQueryable<PluginRelease> query = releases.AsNoTracking()
            .Where(release => release.ListingId == listing.Id);
        if (!canSeeModerationState)
        {
            query = query.Where(release => release.Status == PluginReleaseStatus.Published);
        }

        var values = await query.OrderByDescending(static release => release.UploadedAtUtc)
            .ToArrayAsync(cancellationToken);
        return values.Select(release => release.ToSummary(listing.PluginId)).ToArray();
    }
}

public sealed class UploadPluginRelease(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    MarketCallerResolver callerResolver,
    IPluginPackageStore packageStore,
    AuditWriter auditWriter,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    IDbContext dbContext) : ExtensionMarketService.UploadPluginRelease
{
    public override async Task<Result<PluginReleaseSummary>> HandleAsync(
        UploadPluginReleaseRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Developer | MarketUserRole.Admin, cancellationToken);
        if (caller.User is not { } user)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        string pluginId;
        string version;
        try
        {
            pluginId = PluginListing.NormalizePluginId(request.PluginId);
            version = PluginRelease.NormalizeVersion(request.Version);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin_release", exception.Message);
        }

        var listing = await listings.Where(candidate => candidate.PluginId == pluginId)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null || !HandlerSupport.CanManage(listing, user))
        {
            return Result.Fail("plugin_not_found", "The plugin was not found.");
        }

        if (await releases.AsNoTracking().AnyAsync(
                candidate => candidate.ListingId == listing.Id && candidate.Version == version,
                cancellationToken))
        {
            return Result.Fail("plugin_release_conflict", "This plugin version already exists.");
        }

        StoredPluginPackage storedPackage;
        try
        {
            storedPackage = await packageStore.SaveAsync(
                listing.PluginId,
                version,
                request.PackageArchiveBase64,
                request.ExpectedSha256,
                cancellationToken);
        }
        catch (FormatException)
        {
            return Result.Fail("invalid_plugin_package", "The plugin ZIP archive must be valid Base64.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return Result.Fail("invalid_plugin_package", exception.Message);
        }

        try
        {
            var release = PluginRelease.Create(
                listing.Id,
                version,
                request.QuantumVersionSupport,
                request.ReleaseNotes,
                storedPackage.RelativePath,
                storedPackage.SizeBytes,
                storedPackage.Sha256,
                idGenerator,
                timeProvider);
            await releases.AddAsync(release, cancellationToken);
            listing.Touch();
            await auditWriter.WriteAsync(
                "plugin.release_uploaded",
                user.Id,
                $"Release '{listing.PluginId}@{release.Version}' was uploaded for review.",
                listing.Id,
                release.Id,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return release.ToSummary(listing.PluginId);
        }
        catch (ArgumentException exception)
        {
            await packageStore.DeleteAsync(storedPackage.RelativePath, cancellationToken);
            return Result.Fail("invalid_plugin_release", exception.Message);
        }
        catch
        {
            await packageStore.DeleteAsync(storedPackage.RelativePath, CancellationToken.None);
            throw;
        }
    }
}

public sealed class DownloadPluginRelease(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    IPluginPackageStore packageStore,
    IDbContext dbContext) : ExtensionMarketService.DownloadPluginRelease
{
    public override async Task<Result<DownloadPluginReleaseResponse>> HandleAsync(
        DownloadPluginReleaseRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        string pluginId;
        string version;
        try
        {
            pluginId = PluginListing.NormalizePluginId(request.PluginId);
            version = PluginRelease.NormalizeVersion(request.Version);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_plugin_release", exception.Message);
        }

        var listing = await listings.AsNoTracking()
            .Where(candidate => candidate.PluginId == pluginId)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null)
        {
            return Result.Fail("plugin_release_not_found", "The published plugin release was not found.");
        }

        var release = await releases
            .Where(candidate => candidate.ListingId == listing.Id &&
                candidate.Version == version &&
                candidate.Status == PluginReleaseStatus.Published)
            .SingleOrDefaultAsync(cancellationToken);
        if (release is null)
        {
            return Result.Fail("plugin_release_not_found", "The published plugin release was not found.");
        }

        var archive = await packageStore.ReadAsync(release.PackagePath, cancellationToken);
        release.RecordDownload();
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DownloadPluginReleaseResponse
        {
            FileName = $"{listing.PluginId}-{release.Version}.zip",
            ContentType = "application/zip",
            PackageArchiveBase64 = Convert.ToBase64String(archive),
            PackageSizeBytes = release.PackageSizeBytes,
            PackageSha256 = release.PackageSha256
        };
    }
}

public sealed class CheckCompatibility(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases) : ExtensionMarketService.CheckCompatibility
{
    public override async Task<Result<CompatibilityResponse>> HandleAsync(
        CheckCompatibilityRequest request,
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
            .ToArrayAsync(cancellationToken);
        var latest = published.OrderByDescending(
                static release => release.Version,
                Comparer<string>.Create(QuantumVersionConstraint.CompareSemanticVersions))
            .FirstOrDefault();
        if (latest is null)
        {
            return Result.Fail("plugin_not_found", "The plugin has no published releases.");
        }

        return new CompatibilityResponse
        {
            IsCompatible = QuantumVersionConstraint.Contains(latest.QuantumVersionSupport, request.QuantumVersion),
            MatchedReleaseVersion = latest.Version,
            QuantumVersionSupport = latest.QuantumVersionSupport
        };
    }
}

public sealed class ReviewPluginRelease(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    MarketCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : ExtensionMarketService.ReviewPluginRelease
{
    public override async Task<Result<PluginReleaseSummary>> HandleAsync(
        ReviewPluginReleaseRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Reviewer | MarketUserRole.Admin, cancellationToken);
        if (caller.User is not { } reviewer)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (!HandlerSupport.TryParseReleaseId(request.ReleaseId, out var releaseId))
        {
            return Result.Fail("invalid_release_id", "ReleaseId must be a positive 64-bit integer string.");
        }

        var release = await releases.Where(candidate => candidate.Id == releaseId)
            .SingleOrDefaultAsync(cancellationToken);
        if (release is null)
        {
            return Result.Fail("plugin_release_not_found", "The plugin release was not found.");
        }

        if (release.Status != PluginReleaseStatus.Pending)
        {
            return Result.Fail("plugin_release_already_reviewed", "Only pending releases can be reviewed.");
        }

        if (request.Status is not (PluginReleaseState.Published or PluginReleaseState.Rejected))
        {
            return Result.Fail("invalid_release_status", "A review must publish or reject the release.");
        }

        var listing = await listings.Where(candidate => candidate.Id == release.ListingId)
            .SingleAsync(cancellationToken);
        try
        {
            release.Review((PluginReleaseStatus)(int)request.Status, reviewer.Id, request.Notes);
            listing.Touch();
            await auditWriter.WriteAsync(
                "plugin.release_reviewed",
                reviewer.Id,
                $"Release '{listing.PluginId}@{release.Version}' was {release.Status.ToString().ToLowerInvariant()}.",
                listing.Id,
                release.Id,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return release.ToSummary(listing.PluginId);
        }
        catch (ArgumentException exception)
        {
            return Result.Fail("invalid_release_review", exception.Message);
        }
    }
}

public sealed class ListAllPluginReleases(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    MarketCallerResolver callerResolver) : ExtensionMarketService.ListAllPluginReleases
{
    public override async Task<Result<PluginReleaseSummary[]>> HandleAsync(
        ListAllPluginReleasesRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Reviewer | MarketUserRole.Admin, cancellationToken);
        if (caller.User is null)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (request.Status is { } requestedStatus && !Enum.IsDefined(requestedStatus))
        {
            return Result.Fail("invalid_release_status", "The release status is invalid.");
        }

        IQueryable<PluginRelease> query = releases.AsNoTracking();
        if (request.Status is { } status)
        {
            var domainStatus = (PluginReleaseStatus)(int)status;
            query = query.Where(release => release.Status == domainStatus);
        }

        var values = await query.OrderByDescending(static release => release.UploadedAtUtc)
            .Take(1000)
            .ToArrayAsync(cancellationToken);
        var listingIds = values.Select(static release => release.ListingId).Distinct().ToArray();
        var pluginListings = listingIds.Length == 0
            ? []
            : await listings.AsNoTracking()
                .Where(listing => listingIds.Contains(listing.Id))
                .ToArrayAsync(cancellationToken);
        return values.Select(release => release.ToSummary(
                pluginListings.SingleOrDefault(listing => listing.Id == release.ListingId)?.PluginId))
            .ToArray();
    }
}
