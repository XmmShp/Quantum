using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application.Handlers;

public sealed class RetryPluginReleaseReview(
    IRepository<PluginListing> listings,
    IRepository<PluginRelease> releases,
    PlatformCallerResolver callerResolver,
    AuditWriter auditWriter,
    IDbContext dbContext) : QuantumPlatformService.RetryPluginReleaseReview
{
    public override async Task<Result<PluginReleaseSummary>> HandleAsync(
        RetryPluginReleaseReviewRequest request, Context context, CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(PlatformUserRole.Developer | PlatformUserRole.Admin,
            cancellationToken, QuantumPlatformPermissions.PluginPublish);
        if (caller.User is not { } user) return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        string pluginId, version;
        try { pluginId = PluginListing.NormalizePluginId(request.PluginId); version = PluginRelease.NormalizeVersion(request.Version); }
        catch (ArgumentException exception) { return Result.Fail("invalid_plugin_release", exception.Message); }
        var listing = await listings.Where(item => item.PluginId == pluginId).SingleOrDefaultAsync(cancellationToken);
        if (listing is null || !HandlerSupport.CanManage(listing, user)) return Result.Fail("plugin_not_found", "The plugin was not found.");
        if (callerResolver.IsCredentialClient && !callerResolver.CanPublishPlugin(pluginId))
            return Result.Fail("credential_client_plugin_forbidden", "The client may not publish this plugin.");
        var release = await releases.Where(item => item.ListingId == listing.Id && item.Version == version).SingleOrDefaultAsync(cancellationToken);
        if (release is null) return Result.Fail("plugin_release_not_found", "The release was not found.");
        try { release.QueueAutomatedReview(); }
        catch (InvalidOperationException exception) { return Result.Fail("plugin_review_retry_forbidden", exception.Message); }
        await auditWriter.WriteAsync("plugin.release_review_retried", user.Id,
            $"Automated review requested again for '{pluginId}@{version}' with unchanged package {release.PackageSha256}.", listing.Id, release.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return release.ToSummary(pluginId);
    }
}
