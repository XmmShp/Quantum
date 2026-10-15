using System.Text.RegularExpressions;
using NOF.Domain;

namespace Quantum.Platform.Domain;

public sealed class PasswordResetVerification
{
    public const int MaximumFailedAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(1);

    private static readonly Regex CodePattern = new(
        "^[0-9]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private PasswordResetVerification()
    {
    }

    private PasswordResetVerification(
        PasswordResetVerificationId id,
        string email,
        PlatformUserId userId,
        string codeHash,
        DateTime requestedAtUtc)
    {
        Id = id;
        Email = PlatformUser.NormalizeEmail(email);
        UserId = userId;
        ReplaceCode(codeHash, requestedAtUtc);
    }

    public PasswordResetVerificationId Id { get; private set; }
    public PlatformUserId UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }

    public static PasswordResetVerification Create(
        string email,
        PlatformUserId userId,
        string codeHash,
        IIdGenerator? idGenerator = null,
        TimeProvider? timeProvider = null)
        => new(
            PasswordResetVerificationId.New(idGenerator.OrDefault()),
            email,
            userId,
            codeHash,
            timeProvider.OrDefault().GetUtcNow().UtcDateTime);

    public void ReplaceCode(string codeHash, TimeProvider? timeProvider = null)
        => ReplaceCode(codeHash, timeProvider.OrDefault().GetUtcNow().UtcDateTime);

    public bool CanResend(TimeProvider? timeProvider = null)
        => timeProvider.OrDefault().GetUtcNow().UtcDateTime >= RequestedAtUtc + ResendInterval;

    public bool CanVerify(TimeProvider? timeProvider = null)
        => FailedAttempts < MaximumFailedAttempts &&
            timeProvider.OrDefault().GetUtcNow().UtcDateTime < ExpiresAtUtc;

    public void RecordFailedAttempt()
        => FailedAttempts = checked(FailedAttempts + 1);

    public static string NormalizeCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var normalized = code.Trim();
        if (!CodePattern.IsMatch(normalized))
        {
            throw new ArgumentException("Email verification code must contain exactly six digits.", nameof(code));
        }

        return normalized;
    }

    private void ReplaceCode(string codeHash, DateTime requestedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        CodeHash = codeHash;
        RequestedAtUtc = requestedAtUtc;
        ExpiresAtUtc = requestedAtUtc + Lifetime;
        FailedAttempts = 0;
    }
}
