using System.Globalization;
using System.Security.Cryptography;
using Quantum.Platform.Application;

namespace Quantum.Platform;

public sealed class CryptographicEmailVerificationCodeGenerator : IEmailVerificationCodeGenerator
{
    public string Generate()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}
