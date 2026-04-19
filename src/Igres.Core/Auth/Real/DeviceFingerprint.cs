using System.Security.Cryptography;
using System.Text;

namespace Igres.Core.Auth.Real;

/// <summary>
/// Deterministic device identifiers derived from the username + a local per-install seed.
/// Same user on the same install gets the same fingerprint across launches, which makes the
/// login endpoint less suspicious than rolling a fresh device on every attempt.
/// </summary>
public sealed record DeviceFingerprint(
    string AndroidId,
    string DeviceId,
    string PhoneId,
    string FamilyDeviceId,
    string AdvertisingId,
    string UserAgent,
    string AppVersion,
    string AppVersionCode,
    string Locale,
    string Language,
    string AppId)
{
    public const string DefaultAppId = "567067343352427";
    public const string DefaultAppVersion = "302.0.0.23.114";
    public const string DefaultAppVersionCode = "547519843";
    public const string DefaultLocale = "en_US";
    public const string DefaultLanguage = "en-US";

    /// <summary>
    /// Mobile browser-emulating device string. We pick a common Android device so that the
    /// user-agent blends with typical traffic from the same app version.
    /// </summary>
    private const string DeviceString =
        "29/10; 420dpi; 1080x2137; samsung; SM-G973F; beyond1; exynos9820; en_US; 547519843";

    public static DeviceFingerprint For(string username)
    {
        var seed = DeriveSeed(username);
        var androidId = "android-" + Hex(seed, 0, 8);
        var deviceId = FormatUuid(seed, 8);
        var phoneId = FormatUuid(seed, 24);
        var familyId = FormatUuid(seed, 40);
        var adid = FormatUuid(seed, 56);

        var ua = $"Instagram {DefaultAppVersion} Android ({DeviceString})";
        return new DeviceFingerprint(
            androidId, deviceId, phoneId, familyId, adid,
            ua, DefaultAppVersion, DefaultAppVersionCode,
            DefaultLocale, DefaultLanguage, DefaultAppId);
    }

    private static byte[] DeriveSeed(string username)
    {
        var material = Encoding.UTF8.GetBytes($"igres-device-seed-v1|{username.ToLowerInvariant()}");
        return SHA256.HashData(material).Concat(SHA256.HashData(material.Concat(new byte[] { 0x1 }).ToArray())).ToArray();
    }

    private static string Hex(byte[] source, int offset, int length)
    {
        var sb = new StringBuilder(length * 2);
        for (var i = 0; i < length; i++) sb.Append(source[offset + i].ToString("x2"));
        return sb.ToString();
    }

    private static string FormatUuid(byte[] source, int offset)
    {
        // Take 16 bytes, force RFC 4122 variant/version bits so the result is a valid UUIDv4.
        Span<byte> u = stackalloc byte[16];
        for (var i = 0; i < 16; i++) u[i] = source[(offset + i) % source.Length];
        u[6] = (byte)((u[6] & 0x0F) | 0x40);
        u[8] = (byte)((u[8] & 0x3F) | 0x80);
        return new Guid(u.ToArray()).ToString();
    }
}
