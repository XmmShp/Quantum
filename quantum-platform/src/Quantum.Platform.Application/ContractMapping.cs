using System.Globalization;
using Quantum.Platform.Contract;
using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

internal static class ContractMapping
{
    public static UserSummary ToSummary(this PlatformUser user)
        => new()
        {
            UserId = Format(user.Id),
            Username = user.Username,
            Email = user.Email,
            Roles = (PlatformUserRoles)(int)user.Roles,
            CreatedAtUtc = user.CreatedAtUtc,
            LastLoginAtUtc = user.LastLoginAtUtc
        };

    public static PluginReleaseSummary ToSummary(this PluginRelease release, string? pluginId = null)
        => new()
        {
            ReleaseId = Format(release.Id),
            ListingId = Format(release.ListingId),
            PluginId = pluginId,
            Version = release.Version,
            QuantumVersionSupport = release.QuantumVersionSupport,
            ReleaseNotes = release.ReleaseNotes,
            Status = (PluginReleaseState)(int)release.Status,
            PackageSizeBytes = release.PackageSizeBytes,
            PackageSha256 = release.PackageSha256,
            UploadedAtUtc = release.UploadedAtUtc,
            ReviewedAtUtc = release.ReviewedAtUtc,
            ReviewedByUserId = release.ReviewedByUserId is { } reviewer ? Format(reviewer) : null,
            ReviewNotes = release.ReviewNotes,
            DownloadCount = release.DownloadCount
        };

    public static PluginSummary ToSummary(
        this PluginListing listing,
        PlatformUser? author,
        PluginRelease? latestRelease)
        => new()
        {
            ListingId = Format(listing.Id),
            PluginId = listing.PluginId,
            Name = listing.Name,
            Description = listing.Description,
            AuthorUserId = Format(listing.AuthorUserId),
            AuthorName = author?.Username ?? "Unknown",
            Tags = [.. listing.Tags],
            CreatedAtUtc = listing.CreatedAtUtc,
            UpdatedAtUtc = listing.UpdatedAtUtc,
            LatestRelease = latestRelease?.ToSummary(listing.PluginId)
        };

    public static AuditEntrySummary ToSummary(this AuditEntry entry)
        => new()
        {
            AuditEntryId = Format(entry.Id),
            Action = entry.Action,
            ActorUserId = entry.ActorUserId is { } actor ? Format(actor) : null,
            Details = entry.Details,
            ListingId = entry.ListingId is { } listing ? Format(listing) : null,
            ReleaseId = entry.ReleaseId is { } release ? Format(release) : null,
            OccurredAtUtc = entry.OccurredAtUtc
        };

    public static string Format(PlatformUserId id) => ((long)id).ToString(CultureInfo.InvariantCulture);
    public static string Format(PluginListingId id) => ((long)id).ToString(CultureInfo.InvariantCulture);
    public static string Format(PluginReleaseId id) => ((long)id).ToString(CultureInfo.InvariantCulture);
    public static string Format(AuditEntryId id) => ((long)id).ToString(CultureInfo.InvariantCulture);
}
