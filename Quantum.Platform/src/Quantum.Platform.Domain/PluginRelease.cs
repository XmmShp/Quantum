using System.Text.RegularExpressions;
using NOF.Domain;

namespace Quantum.Platform.Domain;

public enum PluginReleaseStatus
{
    Pending = 1,
    Published,
    Rejected
}

public enum AutomatedReviewStatus
{
    Queued = 1,
    Running,
    Approved,
    Rejected,
    ManualReview,
    Failed
}

public sealed class PluginRelease
{
    private static readonly Regex SemanticVersionPattern = new(
        "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private PluginRelease()
    {
    }

    private PluginRelease(
        PluginReleaseId id,
        PluginListingId listingId,
        string version,
        string quantumVersionSupport,
        string releaseNotes,
        string packagePath,
        long packageSizeBytes,
        string packageSha256,
        DateTime uploadedAtUtc)
    {
        Id = id;
        ListingId = listingId;
        Version = NormalizeVersion(version);
        QuantumVersionSupport = NormalizeQuantumVersionSupport(quantumVersionSupport);
        ReleaseNotes = NormalizeReleaseNotes(releaseNotes);
        PackagePath = RequirePackagePath(packagePath);
        PackageSizeBytes = packageSizeBytes > 0 ? packageSizeBytes : throw new ArgumentOutOfRangeException(nameof(packageSizeBytes));
        PackageSha256 = NormalizeSha256(packageSha256);
        Status = PluginReleaseStatus.Pending;
        AutomatedReviewState = AutomatedReviewStatus.Queued;
        ConcurrencyVersion = 1;
        UploadedAtUtc = uploadedAtUtc;
    }

    public PluginReleaseId Id { get; private set; }
    public PluginListingId ListingId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public string QuantumVersionSupport { get; private set; } = string.Empty;
    public string ReleaseNotes { get; private set; } = string.Empty;
    public string PackagePath { get; private set; } = string.Empty;
    public long PackageSizeBytes { get; private set; }
    public string PackageSha256 { get; private set; } = string.Empty;
    public PluginReleaseStatus Status { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public PlatformUserId? ReviewedByUserId { get; private set; }
    public string? ReviewNotes { get; private set; }
    public long DownloadCount { get; private set; }
    public AutomatedReviewStatus AutomatedReviewState { get; private set; }
    public string? AutomatedReviewTaskId { get; private set; }
    public string? AutomatedReviewSummary { get; private set; }
    public int AutomatedReviewAttempts { get; private set; }
    public DateTime? AutomatedReviewStartedAtUtc { get; private set; }
    public DateTime? AutomatedReviewCompletedAtUtc { get; private set; }
    public int ConcurrencyVersion { get; private set; }

    public static PluginRelease Create(
        PluginListingId listingId,
        string version,
        string quantumVersionSupport,
        string releaseNotes,
        string packagePath,
        long packageSizeBytes,
        string packageSha256,
        IIdGenerator? idGenerator = null,
        TimeProvider? timeProvider = null)
        => new(
            PluginReleaseId.New(idGenerator.OrDefault()),
            listingId,
            version,
            quantumVersionSupport,
            releaseNotes,
            packagePath,
            packageSizeBytes,
            packageSha256,
            timeProvider.OrDefault().GetUtcNow().UtcDateTime);

    public void Review(
        PluginReleaseStatus status,
        PlatformUserId reviewerUserId,
        string? notes,
        TimeProvider? timeProvider = null)
    {
        if (status is not (PluginReleaseStatus.Published or PluginReleaseStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = timeProvider.OrDefault().GetUtcNow().UtcDateTime;
        ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (ReviewNotes?.Length > 2000)
        {
            throw new ArgumentException("Review notes cannot exceed 2000 characters.", nameof(notes));
        }

        if (AutomatedReviewState is AutomatedReviewStatus.Queued or AutomatedReviewStatus.Running or AutomatedReviewStatus.Failed)
        {
            AutomatedReviewState = AutomatedReviewStatus.ManualReview;
            AutomatedReviewCompletedAtUtc = ReviewedAtUtc;
        }

        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
    }

    public void RecordDownload()
    {
        if (Status != PluginReleaseStatus.Published)
        {
            throw new InvalidOperationException("Only published plugin releases can be downloaded.");
        }

        DownloadCount = checked(DownloadCount + 1);
    }

    public void BeginAutomatedReview(TimeProvider? timeProvider = null)
    {
        if (Status != PluginReleaseStatus.Pending || AutomatedReviewState is AutomatedReviewStatus.Approved or AutomatedReviewStatus.Rejected)
        {
            throw new InvalidOperationException("Only an unresolved pending release can enter automated review.");
        }

        AutomatedReviewState = AutomatedReviewStatus.Running;
        AutomatedReviewTaskId = null;
        AutomatedReviewSummary = null;
        AutomatedReviewAttempts = checked(AutomatedReviewAttempts + 1);
        AutomatedReviewStartedAtUtc = timeProvider.OrDefault().GetUtcNow().UtcDateTime;
        AutomatedReviewCompletedAtUtc = null;
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
    }

    public void QueueAutomatedReview()
    {
        if (Status != PluginReleaseStatus.Pending ||
            AutomatedReviewState is not (AutomatedReviewStatus.ManualReview or AutomatedReviewStatus.Failed))
            throw new InvalidOperationException("Only a pending manual or failed review may be retried.");
        AutomatedReviewState = AutomatedReviewStatus.Queued;
        AutomatedReviewTaskId = null;
        AutomatedReviewSummary = null;
        AutomatedReviewStartedAtUtc = null;
        AutomatedReviewCompletedAtUtc = null;
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
    }

    public void RecordAutomatedReviewTask(string taskId)
    {
        if (AutomatedReviewState != AutomatedReviewStatus.Running)
        {
            throw new InvalidOperationException("The release is not being reviewed automatically.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        AutomatedReviewTaskId = taskId.Trim();
        if (AutomatedReviewTaskId.Length > 200)
        {
            throw new ArgumentException("Automated review task ID cannot exceed 200 characters.", nameof(taskId));
        }
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
    }

    public void CompleteAutomatedReview(
        AutomatedReviewStatus result,
        string summary,
        TimeProvider? timeProvider = null)
    {
        if (AutomatedReviewState != AutomatedReviewStatus.Running ||
            result is not (AutomatedReviewStatus.Approved or AutomatedReviewStatus.Rejected or AutomatedReviewStatus.ManualReview))
        {
            throw new InvalidOperationException("The automated review transition is invalid.");
        }

        AutomatedReviewSummary = NormalizeAutomatedReviewSummary(summary);
        AutomatedReviewState = result;
        AutomatedReviewCompletedAtUtc = timeProvider.OrDefault().GetUtcNow().UtcDateTime;
        if (result is AutomatedReviewStatus.Approved or AutomatedReviewStatus.Rejected)
        {
            Status = result == AutomatedReviewStatus.Approved
                ? PluginReleaseStatus.Published
                : PluginReleaseStatus.Rejected;
            ReviewedAtUtc = AutomatedReviewCompletedAtUtc;
            ReviewedByUserId = null;
            ReviewNotes = AutomatedReviewSummary;
        }
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
    }

    public void FailAutomatedReview(string summary, TimeProvider? timeProvider = null)
    {
        if (Status != PluginReleaseStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending release can fail automated review.");
        }

        AutomatedReviewState = AutomatedReviewStatus.Failed;
        AutomatedReviewSummary = NormalizeAutomatedReviewSummary(summary);
        AutomatedReviewCompletedAtUtc = timeProvider.OrDefault().GetUtcNow().UtcDateTime;
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
    }

    private static string NormalizeAutomatedReviewSummary(string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        var normalized = summary.Trim();
        if (normalized.Length > 2000)
        {
            normalized = normalized[..2000];
        }

        return normalized;
    }

    public static string NormalizeVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var normalized = version.Trim();
        if (normalized.Length > 100 || !SemanticVersionPattern.IsMatch(normalized))
        {
            throw new ArgumentException("Plugin version must be a valid semantic version.", nameof(version));
        }

        return normalized;
    }

    private static string NormalizeQuantumVersionSupport(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 200 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Quantum version support expression is invalid.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeReleaseNotes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length > 8000)
        {
            throw new ArgumentException("Release notes cannot exceed 8000 characters.", nameof(value));
        }

        return normalized;
    }

    private static string RequirePackagePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static string NormalizeSha256(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Package SHA-256 must contain 64 hexadecimal characters.", nameof(value));
        }

        return normalized;
    }
}
