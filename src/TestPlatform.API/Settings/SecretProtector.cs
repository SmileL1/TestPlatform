using System.Security.Cryptography;
using System.Text;

namespace TestPlatform.API.Settings;

/// <summary>
/// 密钥本地加密（AES-256-CBC，密钥由固定口令 + 本机机器名派生）。
/// 目的：API Key 不以明文落库。注意密文与机器绑定，换机器需重新填写密钥。
/// </summary>
public static class SecretProtector
{
    private const string Prefix     = "enc:v1:";
    private const string Passphrase = "TestPlatform::secret::v1";   // 混淆口令（非绝对保密，配合机器熵增加破解成本）

    private static byte[] DeriveKey()
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes("TP-salt::" + Environment.MachineName));
        using var kdf = new Rfc2898DeriveBytes(Passphrase, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32);
    }

    /// <summary>加密为 "enc:v1:base64(IV+cipher)"；空串原样返回</summary>
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var data   = Encoding.UTF8.GetBytes(plain);
        var cipher = enc.TransformFinalBlock(data, 0, data.Length);

        var blob = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, blob, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, blob, aes.IV.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    /// <summary>解密；非密文（历史明文 / appsettings 回退）原样返回；失败返回空串</summary>
    public static string Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored ?? "";
        if (!stored.StartsWith(Prefix))   return stored;   // 兼容历史明文与 appsettings.json 回退值

        try
        {
            var blob = Convert.FromBase64String(stored[Prefix.Length..]);
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            var iv = new byte[16];
            Buffer.BlockCopy(blob, 0, iv, 0, 16);
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(blob, 16, blob.Length - 16);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }   // 解密失败（如换了机器）→ 视为未配置
    }
}
