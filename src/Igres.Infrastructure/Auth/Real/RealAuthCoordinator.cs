using System.Collections.Concurrent;
using System.Text.Json;
using Igres.Core.Auth;
using Igres.Core.Auth.Real;
using Igres.Core.Models;
using Igres.Core.Services;
using Igres.Core.Storage;

namespace Igres.Infrastructure.Auth.Real;

/// <summary>
/// Talks to Instagram for real: fetches the current password-encryption public key, signs
/// the password, submits the mobile login endpoint, and on success persists the returned
/// bearer + headers via <see cref="ICapturedHeadersStore"/> so the captured-session
/// provider can immediately replay authenticated requests.
/// </summary>
public sealed class RealAuthCoordinator : IAuthCoordinator, IDisposable
{
    public const string ProviderName = "Instagram (live)";

    private readonly ISecureSessionStore _sessionStore;
    private readonly ICapturedHeadersStore _capturedStore;
    private readonly IUserPreferenceService _prefs;
    private readonly Action? _onCapturedChanged;
    private readonly ConcurrentDictionary<string, PendingLogin> _pending = new();

    public RealAuthCoordinator(
        ISecureSessionStore sessionStore,
        ICapturedHeadersStore capturedStore,
        IUserPreferenceService prefs,
        Action? onCapturedChanged = null)
    {
        _sessionStore = sessionStore;
        _capturedStore = capturedStore;
        _prefs = prefs;
        _onCapturedChanged = onCapturedChanged;
    }

    public async Task<AuthStartResult> StartSignInAsync(AuthStartRequest request, CancellationToken cancellationToken)
    {
        var username = request.Identifier?.Trim();
        var password = request.Secret;
        if (string.IsNullOrWhiteSpace(username))
            return new AuthStartResult(AuthOutcome.Failed, null, null, "Enter your Instagram username to continue.");
        if (string.IsNullOrEmpty(password))
            return new AuthStartResult(AuthOutcome.Failed, null, null, "Enter your Instagram password to continue.");

        var device = DeviceFingerprint.For(username);
        var client = new InstagramLoginClient(device);
        try
        {
            InstagramLoginClient.PubKey pubKey;
            try { pubKey = await client.FetchPublicKeyAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { client.Dispose(); return new AuthStartResult(AuthOutcome.Failed, null, null, $"Could not reach Instagram: {ex.Message}"); }

            var encPassword = InstagramPasswordEncryptor.Encrypt(password, pubKey.KeyId, pubKey.PemOrDer);

            LoginHttpResult resp;
            try { resp = await client.PostLoginAsync(username, encPassword, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { client.Dispose(); return new AuthStartResult(AuthOutcome.Failed, null, null, $"Login request failed: {ex.Message}"); }

            var json = resp.TryJson();
            var status = json?.TryGetProperty("status", out var s) == true ? s.GetString() : null;
            var messageText = json?.TryGetProperty("message", out var m) == true ? m.GetString() : null;

            // 2FA required
            if (json?.TryGetProperty("two_factor_required", out var tfr) == true && tfr.ValueKind == JsonValueKind.True)
            {
                var info = json.Value.GetProperty("two_factor_info");
                var twoFactorId = info.GetProperty("two_factor_identifier").GetString() ?? string.Empty;
                var contact = info.TryGetProperty("obfuscated_phone_number", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : info.TryGetProperty("username", out var u) ? u.GetString() : username;
                var method = info.TryGetProperty("sms_two_factor_on", out var sms) && sms.ValueKind == JsonValueKind.True ? "1"
                    : info.TryGetProperty("totp_two_factor_on", out var totp) && totp.ValueKind == JsonValueKind.True ? "3"
                    : "1";
                var channel = method == "3" ? VerificationChannel.Authenticator : VerificationChannel.Sms;

                var challengeId = Guid.NewGuid().ToString("N");
                var pending = new PendingLogin(username!, device, client, twoFactorId, method);
                _pending[challengeId] = pending;

                var challenge = new VerificationChallenge(
                    challengeId,
                    channel,
                    contact ?? username!,
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    CanResend: false,
                    CanRestart: true);
                return new AuthStartResult(AuthOutcome.ChallengeRequired, null, challenge);
            }

            // Checkpoint / challenge
            if (messageText is "checkpoint_required" or "challenge_required")
            {
                client.Dispose();
                return new AuthStartResult(AuthOutcome.Failed, null, null,
                    "Instagram issued a checkpoint. Open the official mobile app on your phone, resolve the prompt, then try again.");
            }

            // Bad password
            if (json?.TryGetProperty("invalid_credentials", out var ic) == true && ic.ValueKind == JsonValueKind.True)
            {
                client.Dispose();
                return new AuthStartResult(AuthOutcome.Failed, null, null, "Incorrect username or password.");
            }

            if (status == "ok" && json?.TryGetProperty("logged_in_user", out var user) == true)
            {
                var session = await FinalizeAsync(resp, user, username!, device, cancellationToken).ConfigureAwait(false);
                client.Dispose();
                return new AuthStartResult(AuthOutcome.SignedIn, session, null);
            }

            client.Dispose();
            return new AuthStartResult(AuthOutcome.Failed, null, null, messageText ?? "Login failed.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<AuthChallengeResult> SubmitVerificationCodeAsync(string challengeId, string code, CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(challengeId, out var pending))
            return new AuthChallengeResult(AuthOutcome.Failed, null, null, "That 2FA session expired. Restart sign-in.");

        try
        {
            var resp = await pending.Client.PostTwoFactorAsync(pending.Username, code.Trim(), pending.TwoFactorId, pending.VerificationMethod, cancellationToken).ConfigureAwait(false);
            var json = resp.TryJson();
            var status = json?.TryGetProperty("status", out var s) == true ? s.GetString() : null;
            var messageText = json?.TryGetProperty("message", out var m) == true ? m.GetString() : null;

            if (status == "ok" && json?.TryGetProperty("logged_in_user", out var user) == true)
            {
                var session = await FinalizeAsync(resp, user, pending.Username, pending.Device, cancellationToken).ConfigureAwait(false);
                pending.Client.Dispose();
                return new AuthChallengeResult(AuthOutcome.SignedIn, session, null);
            }

            var retry = new VerificationChallenge(
                challengeId,
                VerificationChannel.Sms,
                pending.Username,
                DateTimeOffset.UtcNow.AddMinutes(5),
                CanResend: false,
                CanRestart: true,
                ErrorMessage: messageText ?? "Code rejected. Try again.");
            _pending[challengeId] = pending;
            return new AuthChallengeResult(AuthOutcome.ChallengeRequired, null, retry, retry.ErrorMessage);
        }
        catch (Exception ex)
        {
            _pending[challengeId] = pending;
            return new AuthChallengeResult(AuthOutcome.Failed, null, null, $"2FA submission failed: {ex.Message}");
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        DisposePendingLogins();
        await _sessionStore.ClearSessionAsync(cancellationToken).ConfigureAwait(false);
        await _capturedStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        _onCapturedChanged?.Invoke();
    }

    public Task<AccountSession?> RestoreSessionAsync(CancellationToken cancellationToken) =>
        _sessionStore.LoadSessionAsync(cancellationToken);

    private async Task<AccountSession> FinalizeAsync(LoginHttpResult resp, JsonElement user, string username, DeviceFingerprint device, CancellationToken ct)
    {
        var pk = user.TryGetProperty("pk_id", out var pkid) && pkid.ValueKind == JsonValueKind.String ? pkid.GetString()
            : user.TryGetProperty("pk", out var pkn) && pkn.ValueKind == JsonValueKind.Number ? pkn.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : user.TryGetProperty("pk", out var pks) && pks.ValueKind == JsonValueKind.String ? pks.GetString()
            : string.Empty;
        var displayName = user.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;
        var handle = user.TryGetProperty("username", out var un) ? un.GetString() : username;

        var bearer = resp.HeaderValue("ig-set-authorization") ?? BuildBearerFromCookies(resp.Cookies, pk!);
        var mid = resp.HeaderValue("ig-set-x-mid") ?? resp.Cookies.GetValueOrDefault("mid") ?? string.Empty;
        var rur = resp.HeaderValue("ig-set-ig-u-rur") ?? resp.Cookies.GetValueOrDefault("rur") ?? string.Empty;
        var bloksVersion = resp.HeaderValue("ig-set-x-bloks-version-id") ?? string.Empty;

        var captured = new CapturedHeaders(
            Authorization: bearer!,
            UserAgent: device.UserAgent,
            XIgAppId: "124024574287414",
            XIgDeviceId: device.DeviceId,
            XIgFamilyDeviceId: device.FamilyDeviceId,
            XMid: mid,
            IgUDsUserId: pk!,
            IgIntendedUserId: pk!,
            IgURur: rur,
            XBloksVersionId: bloksVersion,
            AcceptLanguage: device.Language,
            XIgAppLocale: device.Locale,
            XIgDeviceLocale: device.Locale,
            XIgMappedLocale: device.Locale,
            CapturedAt: DateTimeOffset.UtcNow);

        await _capturedStore.SaveAsync(captured, ct).ConfigureAwait(false);

        var currentPrefs = await _prefs.LoadAsync(ct).ConfigureAwait(false);
        if (!currentPrefs.UseCapturedCredentials)
        {
            var updated = currentPrefs with { UseCapturedCredentials = true };
            await _prefs.SaveAsync(updated, ct).ConfigureAwait(false);
        }

        var session = new AccountSession(
            AccountId: pk!,
            DisplayName: string.IsNullOrEmpty(displayName) ? handle! : displayName!,
            Handle: handle!,
            State: AccountSessionState.SignedIn,
            LastAuthenticatedAt: DateTimeOffset.UtcNow,
            HasPersistentCredentials: true,
            ProviderName: ProviderName);
        await _sessionStore.SaveSessionAsync(session, ct).ConfigureAwait(false);

        _onCapturedChanged?.Invoke();
        return session;
    }

    private static string BuildBearerFromCookies(IReadOnlyDictionary<string, string> cookies, string dsUserId)
    {
        if (!cookies.TryGetValue("sessionid", out var sessionId) || string.IsNullOrEmpty(sessionId)) return string.Empty;
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ds_user_id"] = dsUserId,
            ["sessionid"] = sessionId,
            ["should_use_header_over_cookies"] = "true",
        });
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        return "Bearer IGT:2:" + b64;
    }

    public void Dispose() => DisposePendingLogins();

    private void DisposePendingLogins()
    {
        foreach (var challengeId in _pending.Keys)
        {
            if (_pending.TryRemove(challengeId, out var pending))
                pending.Client.Dispose();
        }
    }

    private sealed record PendingLogin(string Username, DeviceFingerprint Device, InstagramLoginClient Client, string TwoFactorId, string VerificationMethod);
}
