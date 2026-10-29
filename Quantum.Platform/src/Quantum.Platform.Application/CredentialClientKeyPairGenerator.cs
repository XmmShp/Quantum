using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantum.Platform.Contract;

namespace Quantum.Platform.Application;

public static class CredentialClientKeyPairGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CredentialClientGeneratedKeySet Generate()
    {
        using var rsa = RSA.Create(3072);
        var parameters = rsa.ExportParameters(true);
        var modulus = Encode(parameters.Modulus!);
        var exponent = Encode(parameters.Exponent!);
        var kid = Encode(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{{\"e\":\"{exponent}\",\"kty\":\"RSA\",\"n\":\"{modulus}\"}}")));
        var publicKey = new { kty = "RSA", use = "sig", alg = "RS256", kid, n = modulus, e = exponent };
        var privateKey = new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid,
            n = modulus,
            e = exponent,
            d = Encode(parameters.D!),
            p = Encode(parameters.P!),
            q = Encode(parameters.Q!),
            dp = Encode(parameters.DP!),
            dq = Encode(parameters.DQ!),
            qi = Encode(parameters.InverseQ!)
        };
        return new CredentialClientGeneratedKeySet
        {
            JsonWebKeySet = JsonSerializer.Serialize(new { keys = new[] { publicKey } }, JsonOptions),
            PrivateJsonWebKeySet = JsonSerializer.Serialize(new { keys = new[] { privateKey } }, JsonOptions)
        };
    }

    private static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
