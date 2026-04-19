using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Igres.Core.Auth.Real;

namespace Igres.Infrastructure.Auth.Real;

/// <summary>
/// Drives the real Instagram mobile-API sign-in: fetches the current password-encryption
/// public key, submits <c>/api/v1/accounts/login/</c>, handles <c>two_factor_required</c>, and
/// returns the raw response alongside the response headers the caller needs to build a
/// session (bearer, mid, rur, cookies).
/// </summary>
public sealed class InstagramLoginClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly DeviceFingerprint _device;
    private readonly CookieContainer _cookies;

    public InstagramLoginClient(DeviceFingerprint device)
    {
        _device = device;
        _cookies = new CookieContainer();
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = _cookies,
            UseCookies = true,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://i.instagram.com"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public record PubKey(byte KeyId, byte[] PemOrDer);

    public async Task<PubKey> FetchPublicKeyAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/qe/sync/");
        ApplyGuestHeaders(req);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.Headers.TryGetValues("ig-set-password-encryption-pub-key", out var keyValues)
            || !resp.Headers.TryGetValues("ig-set-password-encryption-key-id", out var idValues))
        {
            throw new InstagramLoginException("Server did not return a password-encryption public key. The endpoint may be unavailable or rate-limited.");
        }

        var keyB64 = keyValues.First();
        var idStr = idValues.First();
        if (!byte.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyId))
            throw new InstagramLoginException($"Unexpected key id '{idStr}'.");
        return new PubKey(keyId, Convert.FromBase64String(keyB64));
    }

    public async Task<LoginHttpResult> PostLoginAsync(string username, string encPassword, CancellationToken ct)
    {
        var jazoest = InstagramPasswordEncryptor.Jazoest(_device.PhoneId);
        var form = new Dictionary<string, string>
        {
            ["jazoest"] = jazoest,
            ["country_codes"] = "[{\"country_code\":\"1\",\"source\":[\"default\"]}]",
            ["phone_id"] = _device.PhoneId,
            ["enc_password"] = encPassword,
            ["username"] = username,
            ["adid"] = _device.AdvertisingId,
            ["guid"] = _device.DeviceId,
            ["device_id"] = _device.AndroidId,
            ["google_tokens"] = "[]",
            ["login_attempt_count"] = "0",
        };
        return await PostFormAsync("/api/v1/accounts/login/", form, ct).ConfigureAwait(false);
    }

    public async Task<LoginHttpResult> PostTwoFactorAsync(string username, string code, string twoFactorIdentifier, string verificationMethod, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["verification_code"] = code,
            ["phone_id"] = _device.PhoneId,
            ["_csrftoken"] = GetCookie("csrftoken") ?? string.Empty,
            ["two_factor_identifier"] = twoFactorIdentifier,
            ["username"] = username,
            ["trust_this_device"] = "0",
            ["guid"] = _device.DeviceId,
            ["device_id"] = _device.AndroidId,
            ["waterfall_id"] = Guid.NewGuid().ToString(),
            ["verification_method"] = verificationMethod,
        };
        return await PostFormAsync("/api/v1/accounts/two_factor_login/", form, ct).ConfigureAwait(false);
    }

    private async Task<LoginHttpResult> PostFormAsync(string path, IDictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        ApplyGuestHeaders(req);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var headers = resp.Headers
            .SelectMany(h => h.Value.Select(v => (h.Key, v)))
            .ToList();
        return new LoginHttpResult((int)resp.StatusCode, body, headers, SnapshotCookies());
    }

    private void ApplyGuestHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("User-Agent", _device.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Language", _device.Language);
        req.Headers.TryAddWithoutValidation("X-Ig-App-Id", "124024574287414");
        req.Headers.TryAddWithoutValidation("X-Ig-App-Locale", _device.Locale);
        req.Headers.TryAddWithoutValidation("X-Ig-Device-Locale", _device.Locale);
        req.Headers.TryAddWithoutValidation("X-Ig-Mapped-Locale", _device.Locale);
        req.Headers.TryAddWithoutValidation("X-Ig-Device-Id", _device.DeviceId);
        req.Headers.TryAddWithoutValidation("X-Ig-Family-Device-Id", _device.FamilyDeviceId);
        req.Headers.TryAddWithoutValidation("X-Ig-Android-Id", _device.AndroidId);
        req.Headers.TryAddWithoutValidation("X-Ig-Connection-Type", "WIFI");
        req.Headers.TryAddWithoutValidation("X-Ig-Capabilities", "3brTv10=");
        req.Headers.TryAddWithoutValidation("X-Ig-App-Startup-Country", "US");
        req.Headers.TryAddWithoutValidation("X-Ig-Timezone-Offset", "0");
        req.Headers.TryAddWithoutValidation("X-Ig-Bandwidth-Speed-Kbps", "-1.000");
        req.Headers.TryAddWithoutValidation("X-Ig-Bandwidth-TotalBytes-B", "0");
        req.Headers.TryAddWithoutValidation("X-Ig-Bandwidth-TotalTime-MS", "0");
        req.Headers.TryAddWithoutValidation("X-Ig-Www-Claim", GetCookie("x-ig-www-claim") ?? "0");
        req.Headers.TryAddWithoutValidation("X-Pigeon-Session-Id", Guid.NewGuid().ToString());
        req.Headers.TryAddWithoutValidation("X-Pigeon-Rawclienttime",
            (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0).ToString("F3", CultureInfo.InvariantCulture));
        req.Headers.AcceptEncoding.Clear();
        req.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        req.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
    }

    private string? GetCookie(string name)
    {
        var cookies = _cookies.GetCookies(new Uri("https://i.instagram.com"));
        foreach (Cookie c in cookies)
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c.Value;
        return null;
    }

    private Dictionary<string, string> SnapshotCookies()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Cookie c in _cookies.GetCookies(new Uri("https://i.instagram.com")))
            dict[c.Name] = c.Value;
        return dict;
    }

    public void Dispose() => _http.Dispose();
}

public sealed record LoginHttpResult(int StatusCode, string Body, IReadOnlyList<(string Name, string Value)> Headers, IReadOnlyDictionary<string, string> Cookies)
{
    public string? HeaderValue(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    public JsonElement? TryJson()
    {
        try { return JsonDocument.Parse(Body).RootElement.Clone(); }
        catch { return null; }
    }
}

public sealed class InstagramLoginException : Exception
{
    public InstagramLoginException(string message) : base(message) { }
}
