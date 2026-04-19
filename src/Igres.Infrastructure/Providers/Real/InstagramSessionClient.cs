using System.Net.Http.Headers;
using System.Text.Json;
using Igres.Core.Models;

namespace Igres.Infrastructure.Providers.Real;

/// <summary>
/// Replays a captured mobile session against Instagram's private API. The wrapper supports the
/// GETs and form POSTs we need for saved-item cleanup plus Bloks-powered activity-center
/// screens.
/// </summary>
public sealed class InstagramSessionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CapturedHeaders _headers;

    public InstagramSessionClient(CapturedHeaders headers)
    {
        _headers = headers;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            BaseAddress = new Uri("https://i.instagram.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        ApplyStaticHeaders();
    }

    private void ApplyStaticHeaders()
    {
        var h = _http.DefaultRequestHeaders;
        h.TryAddWithoutValidation("Authorization", _headers.Authorization);
        h.TryAddWithoutValidation("User-Agent", _headers.UserAgent);
        h.TryAddWithoutValidation("X-Ig-App-Id", _headers.XIgAppId);
        h.TryAddWithoutValidation("X-Ig-Device-Id", _headers.XIgDeviceId);
        if (!string.IsNullOrEmpty(_headers.XIgFamilyDeviceId))
            h.TryAddWithoutValidation("X-Ig-Family-Device-Id", _headers.XIgFamilyDeviceId);
        h.TryAddWithoutValidation("X-Mid", _headers.XMid);
        h.TryAddWithoutValidation("Ig-U-Ds-User-Id", _headers.IgUDsUserId);
        if (!string.IsNullOrEmpty(_headers.IgIntendedUserId))
            h.TryAddWithoutValidation("Ig-Intended-User-Id", _headers.IgIntendedUserId);
        if (!string.IsNullOrEmpty(_headers.IgURur))
            h.TryAddWithoutValidation("Ig-U-Rur", _headers.IgURur);
        h.TryAddWithoutValidation(
            "X-Bloks-Version-Id",
            InstagramSessionDefaults.ResolveBloksVersionId(_headers.XBloksVersionId));
        if (!string.IsNullOrEmpty(_headers.AcceptLanguage))
            h.TryAddWithoutValidation("Accept-Language", _headers.AcceptLanguage);
        if (!string.IsNullOrEmpty(_headers.XIgAppLocale))
            h.TryAddWithoutValidation("X-Ig-App-Locale", _headers.XIgAppLocale);
        if (!string.IsNullOrEmpty(_headers.XIgDeviceLocale))
            h.TryAddWithoutValidation("X-Ig-Device-Locale", _headers.XIgDeviceLocale);
        if (!string.IsNullOrEmpty(_headers.XIgMappedLocale))
            h.TryAddWithoutValidation("X-Ig-Mapped-Locale", _headers.XIgMappedLocale);
        h.TryAddWithoutValidation("X-Ig-Bloks-Serialize-Payload", "true");
        h.TryAddWithoutValidation("X-Ig-Www-Claim", "SKIP");
        h.Accept.Clear();
        h.Accept.ParseAdd("*/*");
        h.AcceptEncoding.Clear();
        h.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        h.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        h.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
    }

    public Task<string> GetAsync(string pathAndQuery, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, pathAndQuery), ct);

    public Task<string> PostFormAsync(string path, IReadOnlyDictionary<string, string> form, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        return SendAsync(request, ct);
    }

    public Task<string> PostSignedBodyAsync(string path, IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["signed_body"] = "SIGNATURE." + json
            })
        };
        return SendAsync(request, ct);
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            UpdateStickyHeaders(resp);
            EnsureSuccess(request.RequestUri?.PathAndQuery ?? "<unknown>", resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
            return body;
        }
        finally
        {
            request.Dispose();
        }
    }

    private void UpdateStickyHeaders(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Ig-Set-Ig-U-Rur", out var rurValues))
        {
            _http.DefaultRequestHeaders.Remove("Ig-U-Rur");
            var rur = rurValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rur))
                _http.DefaultRequestHeaders.TryAddWithoutValidation("Ig-U-Rur", rur);
        }

        if (response.Headers.TryGetValues("ig-set-x-bloks-version-id", out var bloksValues)
            || response.Headers.TryGetValues("Ig-Set-X-Bloks-Version-Id", out bloksValues))
        {
            _http.DefaultRequestHeaders.Remove("X-Bloks-Version-Id");
            var version = bloksValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(version))
                _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Bloks-Version-Id", version);
        }
    }

    private static void EnsureSuccess(string path, bool isSuccessStatusCode, string body, int statusCode)
    {
        if (!isSuccessStatusCode)
            throw new InstagramSessionException($"HTTP {statusCode} from {path}. Body length: {body.Length}.");

        if (body.Contains("\"message\":\"checkpoint_required\"", StringComparison.Ordinal)
            || body.Contains("\"message\":\"challenge_required\"", StringComparison.Ordinal)
            || body.Contains("\"message\":\"login_required\"", StringComparison.Ordinal))
        {
            throw new InstagramSessionException(
                "Instagram issued a challenge or login_required for this session. Re-capture credentials after resolving the challenge in the mobile app.");
        }

        if (body.Contains("\"status\":\"fail\"", StringComparison.Ordinal))
        {
            var message = TryExtractMessage(body);
            if (!string.IsNullOrWhiteSpace(message))
                throw new InstagramSessionException($"Instagram rejected the request: {message}");

            throw new InstagramSessionException($"Instagram rejected the request to {path}.");
        }
    }

    private static string? TryExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
        }
        catch
        {
            // Best-effort only.
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class InstagramSessionException : Exception
{
    public InstagramSessionException(string message) : base(message) { }
}
