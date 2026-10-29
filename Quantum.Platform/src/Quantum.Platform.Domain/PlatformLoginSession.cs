using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quantum.Platform.Domain;

public sealed class PlatformLoginSession
{
    public Guid Id { get; private set; }
    public PlatformUserId UserId { get; private set; }
    public string PasswordStamp { get; private set; } = "";
    public string TokenHash { get; private set; } = "";
    public string UsedTokensJson { get; private set; } = "{}";
    public Guid? LastRequestId { get; private set; }
    public DateTime? RotatedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public static PlatformLoginSession Create(PlatformUser user, Guid id, string token, DateTime now)
        => new() { Id = id, UserId = user.Id, PasswordStamp = Hash(user.PasswordHash), TokenHash = Hash(token), CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(30) };

    public bool IsActive(PlatformUser user, DateTime now)
        => RevokedAtUtc is null && now < ExpiresAtUtc && UserId == user.Id && PasswordStamp == Hash(user.PasswordHash);

    public RefreshDisposition Inspect(string token, Guid requestId, DateTime now)
    {
        var hash = Hash(token);
        if (CryptographicOperations.FixedTimeEquals(Convert.FromHexString(TokenHash), Convert.FromHexString(hash)))
        {
            return RefreshDisposition.Current;
        }
        var used = JsonSerializer.Deserialize<Dictionary<string, Guid>>(UsedTokensJson)!;
        if (!used.TryGetValue(hash, out var priorRequest))
        {
            return RefreshDisposition.Invalid;
        }
        return priorRequest == requestId && LastRequestId == requestId && RotatedAtUtc > now.AddMinutes(-2)
            ? RefreshDisposition.Retry : RefreshDisposition.Replay;
    }

    public void Rotate(string oldToken, string newToken, Guid requestId, DateTime now)
    {
        var used = JsonSerializer.Deserialize<Dictionary<string, Guid>>(UsedTokensJson)!;
        used.Add(Hash(oldToken), requestId);
        UsedTokensJson = JsonSerializer.Serialize(used);
        TokenHash = Hash(newToken);
        LastRequestId = requestId;
        RotatedAtUtc = now;
    }

    public void Revoke(DateTime now) => RevokedAtUtc ??= now;
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum RefreshDisposition { Current, Retry, Replay, Invalid }
