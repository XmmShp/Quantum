namespace Quantum.Platform.Contract;

public sealed record EmptyRequest;

public sealed record RegisterUserRequest
{
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed record LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed record UpdateCurrentUserRequest
{
    public required string Username { get; init; }
    public required string Email { get; init; }
}

public sealed record GetUserRequest
{
    public required string UserId { get; init; }
}

public sealed record UpdateUserRequest
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
}

public sealed record SetUserRolesRequest
{
    public required string UserId { get; init; }
    public required PlatformUserRoles Roles { get; init; }
}

public sealed record DeleteUserRequest
{
    public required string UserId { get; init; }
}

public sealed record ListPluginsRequest
{
    public string? Search { get; init; }
    public string[] Tags { get; init; } = [];
    public string? AuthorUserId { get; init; }
}

public sealed record GetPluginRequest
{
    public required string PluginId { get; init; }
}

public sealed record CreatePluginRequest
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string[] Tags { get; init; } = [];
}

public sealed record UpdatePluginRequest
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string[] Tags { get; init; } = [];
}

public sealed record DeletePluginRequest
{
    public required string PluginId { get; init; }
}

public sealed record ListPluginReleasesRequest
{
    public required string PluginId { get; init; }
}

public sealed record UploadPluginReleaseRequest
{
    public required string PluginId { get; init; }
    public required string Version { get; init; }
    public required string QuantumVersionSupport { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
    public required string PackageArchiveBase64 { get; init; }
    public string? ExpectedSha256 { get; init; }
}

public sealed record DownloadPluginReleaseRequest
{
    public required string PluginId { get; init; }
    public required string Version { get; init; }
}

public sealed record CheckCompatibilityRequest
{
    public required string PluginId { get; init; }
    public required string QuantumVersion { get; init; }
}

public sealed record ReviewPluginReleaseRequest
{
    public required string ReleaseId { get; init; }
    public required PluginReleaseState Status { get; init; }
    public string? Notes { get; init; }
}

public sealed record ListAllPluginReleasesRequest
{
    public PluginReleaseState? Status { get; init; }
}

public sealed record ListAuditEntriesRequest
{
    public string? ActorUserId { get; init; }
    public string? PluginId { get; init; }
    public string? ReleaseId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public int Limit { get; init; } = 200;
}

public sealed record GetAuditEntryRequest
{
    public required string AuditEntryId { get; init; }
}
