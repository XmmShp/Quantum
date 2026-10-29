namespace Quantum.Platform.Contract;

[Flags]
public enum PlatformUserRoles
{
    None = 0,
    User = 1,
    Developer = 2,
    Reviewer = 4,
    Admin = 8
}

public enum PluginReleaseState
{
    Pending = 1,
    Published,
    Rejected
}

public enum AutomatedReviewState
{
    Queued = 1,
    Running,
    Approved,
    Rejected,
    ManualReview,
    Failed
}

public sealed record UserSummary
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required PlatformUserRoles Roles { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime LastLoginAtUtc { get; init; }
}

public sealed record LoginResponse
{
    public string? RefreshToken { get; init; }
    public DateTime? RefreshExpiresAtUtc { get; init; }
    public required string AccessToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required UserSummary User { get; init; }
    public override string ToString() => "[REDACTED]";
}

public sealed record CredentialClientSummary
{
    public required string ClientId { get; init; }
    public required string DisplayName { get; init; }
    public required string[] AllowedPluginIds { get; init; }
    public required bool IsEnabled { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

public sealed record CredentialClientGeneratedKeySet
{
    public required string PrivateJsonWebKeySet { get; init; }
    public required string JsonWebKeySet { get; init; }

    public override string ToString() => "[REDACTED]";
}

public sealed record CreateCredentialClientResponse
{
    public required CredentialClientSummary Client { get; init; }
    public required CredentialClientGeneratedKeySet GeneratedKeySet { get; init; }
}

public sealed record PluginSummary
{
    public required string ListingId { get; init; }
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string AuthorUserId { get; init; }
    public required string AuthorName { get; init; }
    public required string[] Tags { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public PluginReleaseSummary? LatestRelease { get; init; }
}

public sealed record PluginDetails
{
    public required PluginSummary Plugin { get; init; }
    public required PluginReleaseSummary[] Releases { get; init; }
}

public sealed record PluginReleaseSummary
{
    public required string ReleaseId { get; init; }
    public required string ListingId { get; init; }
    public string? PluginId { get; init; }
    public required string Version { get; init; }
    public required string QuantumVersionSupport { get; init; }
    public required string ReleaseNotes { get; init; }
    public required PluginReleaseState Status { get; init; }
    public required long PackageSizeBytes { get; init; }
    public required string PackageSha256 { get; init; }
    public required DateTime UploadedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public string? ReviewedByUserId { get; init; }
    public string? ReviewNotes { get; init; }
    public required long DownloadCount { get; init; }
    public required AutomatedReviewState AutomatedReviewStatus { get; init; }
    public string? AutomatedReviewTaskId { get; init; }
    public string? AutomatedReviewSummary { get; init; }
    public required int AutomatedReviewAttempts { get; init; }
    public DateTime? AutomatedReviewStartedAtUtc { get; init; }
    public DateTime? AutomatedReviewCompletedAtUtc { get; init; }
}

public sealed record DownloadPluginReleaseResponse
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string PackageArchiveBase64 { get; init; }
    public required long PackageSizeBytes { get; init; }
    public required string PackageSha256 { get; init; }
}

public sealed record CompatibilityResponse
{
    public required bool IsCompatible { get; init; }
    public string? MatchedReleaseVersion { get; init; }
    public string? QuantumVersionSupport { get; init; }
}

public sealed record AuditEntrySummary
{
    public required string AuditEntryId { get; init; }
    public required string Action { get; init; }
    public string? ActorUserId { get; init; }
    public required string Details { get; init; }
    public string? ListingId { get; init; }
    public string? ReleaseId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
}
