using System.Text.RegularExpressions;

namespace Igres.Core.Logging;

public interface IRedactionService
{
    string Redact(string input);
    string RedactException(Exception exception);
}

public sealed partial class RedactionService : IRedactionService
{
    private static readonly string[] SensitiveKeys =
    {
        "authorization", "cookie", "set-cookie", "x-ig-app-id", "x-fb-", "session",
        "token", "bearer", "password", "secret", "csrf", "context_data", "ds_user_id",
        "sessionid", "mid", "ig_did"
    };

    [GeneratedRegex(@"(?i)(authorization|cookie|set-cookie|token|bearer|password|secret|csrf|session[_-]?id|sessionid|ds_user_id|mid|ig_did|context_data)\s*[:=]\s*[^\s,;]+", RegexOptions.Compiled)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(?i)""(access_token|refresh_token|id_token|bearer|token|password|secret|sessionid|ds_user_id)""\s*:\s*""[^""]+""", RegexOptions.Compiled)]
    private static partial Regex JsonFieldPattern();

    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var redacted = HeaderPattern().Replace(input, m =>
        {
            var sep = m.Value.Contains(':') ? ":" : "=";
            var key = m.Value.Split(new[] { ':', '=' }, 2)[0].Trim();
            return $"{key}{sep} [REDACTED]";
        });
        redacted = JwtPattern().Replace(redacted, "[REDACTED-JWT]");
        redacted = JsonFieldPattern().Replace(redacted, m =>
        {
            var field = m.Value.Split('"')[1];
            return $"\"{field}\": \"[REDACTED]\"";
        });
        return redacted;
    }

    public string RedactException(Exception exception)
    {
        if (exception is null) return string.Empty;
        return Redact($"{exception.GetType().Name}: {exception.Message}");
    }

    public static IReadOnlyList<string> SensitiveHeaderNames => SensitiveKeys;
}
