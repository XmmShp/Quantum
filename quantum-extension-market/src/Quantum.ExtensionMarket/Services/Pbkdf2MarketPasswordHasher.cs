using System.Globalization;
using System.Security.Cryptography;
using Quantum.ExtensionMarket.Application;

namespace Quantum.ExtensionMarket;

public sealed class Pbkdf2MarketPasswordHasher : IMarketPasswordHasher
{
    private const int IterationCount = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Scheme = "pbkdf2-sha256";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            IterationCount,
            HashAlgorithmName.SHA256,
            KeySize);
        return string.Join(
            '$',
            Scheme,
            IterationCount.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public bool Verify(string passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(password);
        var segments = passwordHash.Split('$');
        if (segments.Length != 4 || !string.Equals(segments[0], Scheme, StringComparison.Ordinal) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations is < 100_000 or > 1_000_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(segments[2]);
            var expected = Convert.FromBase64String(segments[3]);
            if (salt.Length != SaltSize || expected.Length != KeySize)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
