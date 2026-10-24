using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Quantum.Platform.Application;

namespace Quantum.Platform.Authentication;

public sealed class RefreshTokenCodec(IOptions<PlatformJwtOptions> options) : IRefreshTokenCodec
{
    public string Create(Guid sessionId) => sessionId.ToString("N") + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string Rotate(string token, Guid requestId)
        => token[..33] + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.SigningKey),
            Encoding.UTF8.GetBytes("quantum-refresh-v1:" + token + ":" + requestId.ToString("N"))));
}
