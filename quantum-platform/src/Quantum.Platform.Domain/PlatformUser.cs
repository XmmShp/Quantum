using System.Net.Mail;
using NOF.Domain;

namespace Quantum.Platform.Domain;

[Flags]
public enum PlatformUserRole
{
    None = 0,
    User = 1,
    Developer = 2,
    Reviewer = 4,
    Admin = 8
}

public sealed class PlatformUser
{
    private PlatformUser()
    {
    }

    private PlatformUser(
        PlatformUserId id,
        string username,
        string email,
        string passwordHash,
        PlatformUserRole roles,
        DateTime createdAtUtc)
    {
        Id = id;
        Username = NormalizeUsername(username);
        Email = NormalizeEmail(email);
        PasswordHash = RequirePasswordHash(passwordHash);
        Roles = ValidateRoles(roles);
        CreatedAtUtc = createdAtUtc;
        LastLoginAtUtc = createdAtUtc;
    }

    public PlatformUserId Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public PlatformUserRole Roles { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastLoginAtUtc { get; private set; }

    public static PlatformUser Create(
        string username,
        string email,
        string passwordHash,
        PlatformUserRole roles = PlatformUserRole.User | PlatformUserRole.Developer,
        IIdGenerator? idGenerator = null,
        TimeProvider? timeProvider = null)
        => new(
            PlatformUserId.New(idGenerator.OrDefault()),
            username,
            email,
            passwordHash,
            roles,
            timeProvider.OrDefault().GetUtcNow().UtcDateTime);

    public void UpdateProfile(string username, string email)
    {
        Username = NormalizeUsername(username);
        Email = NormalizeEmail(email);
    }

    public void SetRoles(PlatformUserRole roles) => Roles = ValidateRoles(roles);

    public void RecordLogin(TimeProvider? timeProvider = null)
        => LastLoginAtUtc = timeProvider.OrDefault().GetUtcNow().UtcDateTime;

    public bool HasAnyRole(PlatformUserRole roles) => (Roles & roles) != 0;

    public static string NormalizeUsername(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var normalized = username.Trim();
        if (normalized.Length is < 3 or > 64 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Username must contain 3 to 64 visible characters.", nameof(username));
        }

        return normalized;
    }

    public static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 320 || !MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Email address is invalid.", nameof(email));
        }

        return normalized;
    }

    private static string RequirePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return passwordHash;
    }

    private static PlatformUserRole ValidateRoles(PlatformUserRole roles)
    {
        const PlatformUserRole all = PlatformUserRole.User | PlatformUserRole.Developer |
                                   PlatformUserRole.Reviewer | PlatformUserRole.Admin;
        if (roles == PlatformUserRole.None || (roles & ~all) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roles));
        }

        return roles;
    }
}
