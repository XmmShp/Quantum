using System.Security.Cryptography;
using System.Text;

namespace Quantum.Sdk.Utilities;

/// <summary>
/// 加密工具类，提供基于设备特定密钥的AES加密和解密功能
/// </summary>
public static class Encryption
{
    /// <summary>
    /// 设备特定的密钥和初始化向量，基于设备名称生成
    /// </summary>
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

    /// <summary>
    /// 使用AES算法加密字符串
    /// </summary>
    /// <param name="plainText">要加密的明文</param>
    /// <returns>Base64编码的密文</returns>
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

    /// <summary>
    /// 使用AES算法解密字符串
    /// </summary>
    /// <param name="cipherText">要解密的Base64编码密文</param>
    /// <returns>解密后的明文</returns>
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
