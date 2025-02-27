using System.Security.Cryptography;
using System.Text;

namespace Quantum.Sdk.Utilities;

public static class Encryption
{
    private static readonly Lazy<(byte[] Key, byte[] IV)> DeviceSpecificKeyIv = new(() =>
    {
        var deviceId = Environment.MachineName;
        using var deriveBytes = new Rfc2898DeriveBytes(
            deviceId,
            "QuantumSalt"u8.ToArray(),
            10000,
            HashAlgorithmName.SHA256);

        return (deriveBytes.GetBytes(32), deriveBytes.GetBytes(16));
    });

    public static string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = DeviceSpecificKeyIv.Value.Key;
        aes.IV = DeviceSpecificKeyIv.Value.IV;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return Convert.ToBase64String(cipherBytes);
    }

    public static string Decrypt(string cipherText)
    {
        using var aes = Aes.Create();
        aes.Key = DeviceSpecificKeyIv.Value.Key;
        aes.IV = DeviceSpecificKeyIv.Value.IV;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
