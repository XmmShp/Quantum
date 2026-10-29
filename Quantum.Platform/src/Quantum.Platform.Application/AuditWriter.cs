using NOF.Domain;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public sealed class AuditWriter(
    IRepository<AuditEntry> auditEntries,
    IIdGenerator idGenerator,
    TimeProvider timeProvider)
{
    public async Task WriteAsync(
        string action,
        PlatformUserId? actorUserId,
        string details,
        PluginListingId? listingId,
        PluginReleaseId? releaseId,
        CancellationToken cancellationToken)
        => await auditEntries.AddAsync(
            AuditEntry.Create(action, actorUserId, details, listingId, releaseId, idGenerator, timeProvider),
            cancellationToken);
}
