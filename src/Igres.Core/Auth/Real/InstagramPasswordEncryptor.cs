using System.Security.Cryptography;
using System.Text;

namespace Igres.Core.Auth.Real;

/// <summary>
/// Produces the <c>#PWD_INSTAGRAM:4:&lt;ts&gt;:&lt;b64&gt;</c> string Instagram expects in
/// the <c>enc_password</c> field. Uses a random 32-byte AES key sealed with the server's RSA
/// public key and AES-256-GCM over the password with the Unix timestamp as AAD. Binary layout
/// (little-endian): <c>[0x01][key_id][iv:12][rsa_key_len:2][rsa_encrypted_key][gcm_tag:16][ct]</c>.
/// </summary>
public static class InstagramPasswordEncryptor
{
    public static string Encrypt(string password, byte keyId, byte[] rsaPublicKeyDerOrPem, DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentNullException.ThrowIfNull(rsaPublicKeyDerOrPem);

        var ts = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var timestamp = ts.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var sessionKey = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(12);

        using var rsa = RSA.Create();
        ImportPublicKey(rsa, rsaPublicKeyDerOrPem);
        var rsaEncryptedKey = rsa.Encrypt(sessionKey, RSAEncryptionPadding.Pkcs1);

        var plaintext = Encoding.UTF8.GetBytes(password);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(sessionKey, 16))
        {
            gcm.Encrypt(iv, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(timestamp));
        }

        var rsaLen = (ushort)rsaEncryptedKey.Length;
        var payload = new byte[1 + 1 + iv.Length + 2 + rsaEncryptedKey.Length + tag.Length + ciphertext.Length];
        var o = 0;
        payload[o++] = 0x01;
        payload[o++] = keyId;
        Buffer.BlockCopy(iv, 0, payload, o, iv.Length); o += iv.Length;
        payload[o++] = (byte)(rsaLen & 0xff);
        payload[o++] = (byte)((rsaLen >> 8) & 0xff);
        Buffer.BlockCopy(rsaEncryptedKey, 0, payload, o, rsaEncryptedKey.Length); o += rsaEncryptedKey.Length;
        Buffer.BlockCopy(tag, 0, payload, o, tag.Length); o += tag.Length;
        Buffer.BlockCopy(ciphertext, 0, payload, o, ciphertext.Length);

        return $"#PWD_INSTAGRAM:4:{timestamp}:{Convert.ToBase64String(payload)}";
    }

    private static void ImportPublicKey(RSA rsa, byte[] material)
    {
        // Server sends the PEM text base64-encoded in a response header. After base64-decoding
        // we may already be looking at the PEM ASCII armor; if not, try DER directly.
        var text = Encoding.UTF8.GetString(material).Trim();
        if (text.StartsWith("-----BEGIN", StringComparison.Ordinal))
        {
            rsa.ImportFromPem(text);
            return;
        }
        try { rsa.ImportSubjectPublicKeyInfo(material, out _); return; }
        catch { /* fall through */ }
        rsa.ImportRSAPublicKey(material, out _);
    }

    /// <summary>jazoest = "2" + sum of UTF-8 byte values of the phone_id UUID.</summary>
    public static string Jazoest(string phoneId)
    {
        var sum = 0;
        foreach (var b in Encoding.UTF8.GetBytes(phoneId)) sum += b;
        return "2" + sum.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
