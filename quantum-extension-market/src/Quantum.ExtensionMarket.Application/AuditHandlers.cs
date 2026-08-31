using NOF.Contract;
using NOF.Domain;
using Quantum.ExtensionMarket.Contract;
using Quantum.ExtensionMarket.Domain;

namespace Quantum.ExtensionMarket.Application.Handlers;

public sealed class ListAuditEntries(
    IRepository<AuditEntry> auditEntries,
    IRepository<PluginListing> listings,
    MarketCallerResolver callerResolver) : ExtensionMarketService.ListAuditEntries
{
    public override async Task<Result<AuditEntrySummary[]>> HandleAsync(
        ListAuditEntriesRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Admin, cancellationToken);
        if (caller.User is null)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        MarketUserId? actorId = null;
        if (!string.IsNullOrWhiteSpace(request.ActorUserId))
        {
            if (!HandlerSupport.TryParseUserId(request.ActorUserId, out var parsedActorId))
            {
                return Result.Fail("invalid_actor_user_id", "ActorUserId must be a positive 64-bit integer string.");
            }

            actorId = parsedActorId;
        }

        PluginReleaseId? releaseId = null;
        if (!string.IsNullOrWhiteSpace(request.ReleaseId))
        {
            if (!HandlerSupport.TryParseReleaseId(request.ReleaseId, out var parsedReleaseId))
            {
                return Result.Fail("invalid_release_id", "ReleaseId must be a positive 64-bit integer string.");
            }

            releaseId = parsedReleaseId;
        }

        PluginListingId? listingId = null;
        if (!string.IsNullOrWhiteSpace(request.PluginId))
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

            var foundListing = await listings.AsNoTracking()
                .Where(candidate => candidate.PluginId == pluginId)
                .SingleOrDefaultAsync(cancellationToken);
            if (foundListing is null)
            {
                return Array.Empty<AuditEntrySummary>();
            }

            listingId = foundListing.Id;
        }

        if (request.FromUtc is { } from && request.ToUtc is { } to && from > to)
        {
            return Result.Fail("invalid_audit_range", "FromUtc cannot be later than ToUtc.");
        }

        var limit = request.Limit <= 0 ? 200 : Math.Min(request.Limit, 1000);
        IQueryable<AuditEntry> query = auditEntries.AsNoTracking();
        if (actorId is { } actor)
        {
            query = query.Where(entry => entry.ActorUserId == actor);
        }

        if (listingId is { } listing)
        {
            query = query.Where(entry => entry.ListingId == listing);
        }

        if (releaseId is { } release)
        {
            query = query.Where(entry => entry.ReleaseId == release);
        }

        if (request.FromUtc is { } fromUtc)
        {
            query = query.Where(entry => entry.OccurredAtUtc >= fromUtc);
        }

        if (request.ToUtc is { } toUtc)
        {
            query = query.Where(entry => entry.OccurredAtUtc <= toUtc);
        }

        var entries = await query.OrderByDescending(static entry => entry.OccurredAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return entries.Select(static entry => entry.ToSummary()).ToArray();
    }
}

public sealed class GetAuditEntry(
    IRepository<AuditEntry> auditEntries,
    MarketCallerResolver callerResolver) : ExtensionMarketService.GetAuditEntry
{
    public override async Task<Result<AuditEntrySummary>> HandleAsync(
        GetAuditEntryRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var caller = await callerResolver.RequireAsync(MarketUserRole.Admin, cancellationToken);
        if (caller.User is null)
        {
            return Result.Fail(caller.ErrorCode!, caller.ErrorMessage!);
        }

        if (!HandlerSupport.TryParseAuditEntryId(request.AuditEntryId, out var auditEntryId))
        {
            return Result.Fail(
                "invalid_audit_entry_id",
                "AuditEntryId must be a positive 64-bit integer string.");
        }

        var entry = await auditEntries.AsNoTracking()
            .Where(candidate => candidate.Id == auditEntryId)
            .SingleOrDefaultAsync(cancellationToken);
        return entry is null
            ? Result.Fail("audit_entry_not_found", "The audit entry was not found.")
            : entry.ToSummary();
    }
}
