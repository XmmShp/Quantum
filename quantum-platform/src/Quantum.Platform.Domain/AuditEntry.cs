using NOF.Domain;

namespace Quantum.Platform.Domain;

public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    private AuditEntry(
        AuditEntryId id,
        string action,
        PlatformUserId? actorUserId,
        string details,
        PluginListingId? listingId,
        PluginReleaseId? releaseId,
        DateTime occurredAtUtc)
    {
        Id = id;
        Action = action;
        ActorUserId = actorUserId;
        Details = details;
        ListingId = listingId;
        ReleaseId = releaseId;
        OccurredAtUtc = occurredAtUtc;
    }

    public AuditEntryId Id { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public PlatformUserId? ActorUserId { get; private set; }
    public string Details { get; private set; } = string.Empty;
    public PluginListingId? ListingId { get; private set; }
    public PluginReleaseId? ReleaseId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static AuditEntry Create(
        string action,
        PlatformUserId? actorUserId,
        string details,
        PluginListingId? listingId = null,
        PluginReleaseId? releaseId = null,
        IIdGenerator? idGenerator = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(details);
        var normalizedAction = action.Trim();
        var normalizedDetails = details.Trim();
        if (normalizedAction.Length > 100 || normalizedDetails.Length > 4000)
        {
            throw new ArgumentException("Audit entry is too large.");
        }

        return new AuditEntry(
            AuditEntryId.New(idGenerator.OrDefault()),
            normalizedAction,
            actorUserId,
            normalizedDetails,
            listingId,
            releaseId,
            timeProvider.OrDefault().GetUtcNow().UtcDateTime);
    }
}
