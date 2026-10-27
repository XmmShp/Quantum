using Quantum.Platform.Domain;

namespace Quantum.Platform.Application;

public interface IPlatformCallerContext
{
    string? UserId { get; }
    string? CredentialClientId { get; }
    string? LoginSessionId => null;
    bool HasPermission(string permission);
}

public interface ICredentialClientProtocolRepository
{
    Task<IReadOnlyList<CredentialClientProtocolDescriptor>> ListAsync(CancellationToken cancellationToken = default);
    Task<NOF.Contract.Result<CredentialClientProtocolDescriptor>> CreateAsync(
        CreateCredentialClientProtocolRequest request,
        CancellationToken cancellationToken = default);
    Task<NOF.Contract.Result> DeleteAsync(string clientId, CancellationToken cancellationToken = default);
}

public sealed record CredentialClientProtocolDescriptor(
    string ClientId,
    string DisplayName,
    string OwnerUserId,
    IReadOnlyList<string> AllowedPluginIds,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateCredentialClientProtocolRequest(
    string ClientId,
    string DisplayName,
    string OwnerUserId,
    IReadOnlyList<string> AllowedPluginIds,
    string JsonWebKeySet);

public interface IPlatformPasswordHasher
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}

public interface IPlatformTokenIssuer
{
    IssuedAccessToken Issue(PlatformUser user, Guid? sessionId = null);
}

public interface ILoginSessionValidator
{
    Task<bool> IsActiveAsync(PlatformUser user, string sessionId, CancellationToken cancellationToken);
}

public interface IRefreshTokenCodec
{
    string Create(Guid sessionId);
    string Rotate(string token, Guid requestId);
}

public interface IPlatformEmailSender
{
    Task SendAsync(
        string recipient,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken);
}

public interface IEmailVerificationCodeGenerator
{
    string Generate();
}

public interface IRegistrationLock
{
    Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken cancellationToken);
}

public sealed record IssuedAccessToken(string AccessToken, DateTime ExpiresAtUtc);

public enum AutomatedPluginReviewDecision
{
    Approve,
    Reject,
    ManualReview
}

public sealed record AutomatedPluginReviewResult(
    string TaskId,
    AutomatedPluginReviewDecision Decision,
    string Summary);

public interface IPluginReleaseAutomatedReviewer
{
    Task<string> StartAsync(
        PluginListing listing,
        PluginRelease release,
        byte[] packageArchive,
        CancellationToken cancellationToken);

    Task<AutomatedPluginReviewResult> WaitAsync(
        string taskId,
        CancellationToken cancellationToken);
}

public interface IPluginPackageStore
{
    Task<StoredPluginPackage> SaveAsync(
        string pluginId,
        string version,
        string archiveBase64,
        string? expectedSha256,
        CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(string relativePath, CancellationToken cancellationToken);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record StoredPluginPackage(
    string RelativePath,
    long SizeBytes,
    string Sha256);

public sealed class PluginStorageOptions
{
    public const string SectionName = "QuantumPlatform:Storage";

    public string BasePath { get; set; } = "Files";
    public string Provider { get; set; } = "Physical";
    public long MaxArchiveBytes { get; set; } = 256L * 1024 * 1024;
    public long MaxExpandedBytes { get; set; } = 1024L * 1024 * 1024;
    public int MaxEntries { get; set; } = 5000;
}
