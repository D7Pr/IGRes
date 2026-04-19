using Igres.Core.Models;

namespace Igres.Core.Providers.Real;

public static class CapturedRequestParser
{
    /// <summary>
    /// Parse a raw HTTP request block (headers section, one header per line, <c>Key: value</c>)
    /// into a <see cref="CapturedHeaders"/>. Missing required headers yield a descriptive exception
    /// so the UI can tell the user which one to add.
    /// </summary>
    public static CapturedHeaders Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new CapturedRequestParseException("Empty input.");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            // Skip the request line / prefix labels.
            if (line.StartsWith("request:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("response:", StringComparison.OrdinalIgnoreCase)) break;
            if (line.StartsWith("GET ", StringComparison.Ordinal)
                || line.StartsWith("POST ", StringComparison.Ordinal)
                || line.StartsWith("HTTP/", StringComparison.Ordinal))
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0) continue;
            map[key] = value;
        }

        string Require(string key)
        {
            if (!map.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
                throw new CapturedRequestParseException($"Missing required header '{key}'.");
            return v;
        }
        string Optional(string key) => map.TryGetValue(key, out var v) ? v : string.Empty;

        return new CapturedHeaders(
            Authorization: Require("Authorization"),
            UserAgent: Require("User-Agent"),
            XIgAppId: Require("X-Ig-App-Id"),
            XIgDeviceId: Require("X-Ig-Device-Id"),
            XIgFamilyDeviceId: Optional("X-Ig-Family-Device-Id"),
            XMid: Require("X-Mid"),
            IgUDsUserId: Require("Ig-U-Ds-User-Id"),
            IgIntendedUserId: Optional("Ig-Intended-User-Id"),
            IgURur: Optional("Ig-U-Rur"),
            XBloksVersionId: Optional("X-Bloks-Version-Id"),
            AcceptLanguage: Optional("Accept-Language"),
            XIgAppLocale: Optional("X-Ig-App-Locale"),
            XIgDeviceLocale: Optional("X-Ig-Device-Locale"),
            XIgMappedLocale: Optional("X-Ig-Mapped-Locale"),
            CapturedAt: DateTimeOffset.UtcNow);
    }
}

public sealed class CapturedRequestParseException : Exception
{
    public CapturedRequestParseException(string message) : base(message) { }
}
