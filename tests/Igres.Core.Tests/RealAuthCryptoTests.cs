using System.Security.Cryptography;
using System.Text;
using Igres.Core.Auth.Real;

namespace Igres.Core.Tests;

public class RealAuthCryptoTests
{
    [Fact]
    public void DeviceFingerprint_is_deterministic_per_username()
    {
        var a = DeviceFingerprint.For("meow11z1");
        var b = DeviceFingerprint.For("meow11z1");
        a.AndroidId.Should().Be(b.AndroidId);
        a.DeviceId.Should().Be(b.DeviceId);
        a.PhoneId.Should().Be(b.PhoneId);
        a.FamilyDeviceId.Should().Be(b.FamilyDeviceId);
    }

    [Fact]
    public void DeviceFingerprint_varies_per_username()
    {
        var a = DeviceFingerprint.For("meow11z1");
        var b = DeviceFingerprint.For("other_user");
        a.AndroidId.Should().NotBe(b.AndroidId);
        a.DeviceId.Should().NotBe(b.DeviceId);
    }

    [Fact]
    public void DeviceFingerprint_produces_uuid_shaped_ids()
    {
        var d = DeviceFingerprint.For("anyone");
        Guid.TryParse(d.DeviceId, out _).Should().BeTrue();
        Guid.TryParse(d.PhoneId, out _).Should().BeTrue();
        Guid.TryParse(d.FamilyDeviceId, out _).Should().BeTrue();
        d.AndroidId.Should().StartWith("android-");
        d.AndroidId.Length.Should().Be("android-".Length + 16);
    }

    [Fact]
    public void Jazoest_is_two_prefix_plus_phoneid_byte_sum()
    {
        var phoneId = "abc";
        // 'a'=97, 'b'=98, 'c'=99 → 294
        InstagramPasswordEncryptor.Jazoest(phoneId).Should().Be("2294");
    }

    [Fact]
    public void Encrypt_produces_prefix_and_decrypts_round_trip()
    {
        using var rsa = RSA.Create(2048);
        var spki = rsa.ExportSubjectPublicKeyInfo();

        var encoded = InstagramPasswordEncryptor.Encrypt("correct horse battery staple", keyId: 188, rsaPublicKeyDerOrPem: spki);
        encoded.Should().StartWith("#PWD_INSTAGRAM:4:");

        var parts = encoded.Split(':', 4);
        parts.Length.Should().Be(4);
        var timestamp = parts[2];
        var payload = Convert.FromBase64String(parts[3]);

        // Parse: [0x01][keyId][iv:12][rsaLen:2 LE][rsaKey][tag:16][ct]
        payload[0].Should().Be(0x01);
        payload[1].Should().Be(188);
        var iv = payload[2..14];
        var rsaLen = payload[14] | (payload[15] << 8);
        var rsaKey = payload[16..(16 + rsaLen)];
        var tag = payload[(16 + rsaLen)..(16 + rsaLen + 16)];
        var ct = payload[(16 + rsaLen + 16)..];

        var sessionKey = rsa.Decrypt(rsaKey, RSAEncryptionPadding.Pkcs1);
        sessionKey.Length.Should().Be(32);

        var plaintext = new byte[ct.Length];
        using var gcm = new AesGcm(sessionKey, 16);
        gcm.Decrypt(iv, ct, tag, plaintext, Encoding.UTF8.GetBytes(timestamp));
        Encoding.UTF8.GetString(plaintext).Should().Be("correct horse battery staple");
    }
}
