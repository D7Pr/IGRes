using System.Collections.Concurrent;
using Igres.Core.Auth;
using Igres.Core.Models;
using Igres.Core.Storage;

namespace Igres.Infrastructure.Auth;

public sealed class MockAuthCoordinator : IAuthCoordinator
{
    public const string ExpectedVerificationCode = "123456";
    public const string ProviderName = "Mock";

    private readonly ISecureSessionStore _store;
    private readonly ConcurrentDictionary<string, PendingChallenge> _challenges = new();

    public MockAuthCoordinator(ISecureSessionStore store)
    {
        _store = store;
    }

    public Task<AuthStartResult> StartSignInAsync(AuthStartRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            return Task.FromResult(new AuthStartResult(AuthOutcome.Failed, null, null, "Enter an identifier to continue."));
        }
        var challengeId = Guid.NewGuid().ToString("N");
        var challenge = new VerificationChallenge(
            challengeId,
            VerificationChannel.Email,
            ObfuscateIdentifier(request.Identifier),
            DateTimeOffset.UtcNow.AddMinutes(5),
            CanResend: true,
            CanRestart: true);
        _challenges[challengeId] = new PendingChallenge(challenge, request.Identifier);
        return Task.FromResult(new AuthStartResult(AuthOutcome.ChallengeRequired, null, challenge));
    }

    public async Task<AuthChallengeResult> SubmitVerificationCodeAsync(string challengeId, string code, CancellationToken cancellationToken)
    {
        if (!_challenges.TryGetValue(challengeId, out var pending))
        {
            return new AuthChallengeResult(AuthOutcome.Failed, null, null, "That verification session is no longer valid. Please restart sign-in.");
        }
        if (pending.Challenge.ExpiresAt is { } expires && expires < DateTimeOffset.UtcNow)
        {
            _challenges.TryRemove(challengeId, out _);
            return new AuthChallengeResult(AuthOutcome.Failed, null, null, "Verification code expired. Please request a new code.");
        }
        if (!string.Equals(code?.Trim(), ExpectedVerificationCode, StringComparison.Ordinal))
        {
            var updated = pending.Challenge with { ErrorMessage = "Incorrect code. Try again." };
            _challenges[challengeId] = pending with { Challenge = updated };
            return new AuthChallengeResult(AuthOutcome.ChallengeRequired, null, updated, updated.ErrorMessage);
        }

        _challenges.TryRemove(challengeId, out _);
        var session = new AccountSession(
            AccountId: $"mock-{Math.Abs(pending.Identifier.GetHashCode()):x}",
            DisplayName: string.IsNullOrWhiteSpace(pending.Identifier) ? "Mock User" : pending.Identifier,
            Handle: pending.Identifier,
            State: AccountSessionState.SignedIn,
            LastAuthenticatedAt: DateTimeOffset.UtcNow,
            HasPersistentCredentials: true,
            ProviderName: ProviderName);
        await _store.SaveSessionAsync(session, cancellationToken);
        return new AuthChallengeResult(AuthOutcome.SignedIn, session, null);
    }

    public Task SignOutAsync(CancellationToken cancellationToken) => _store.ClearSessionAsync(cancellationToken);

    public Task<AccountSession?> RestoreSessionAsync(CancellationToken cancellationToken) => _store.LoadSessionAsync(cancellationToken);

    private static string ObfuscateIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return string.Empty;
        if (identifier.Contains('@'))
        {
            var parts = identifier.Split('@', 2);
            var name = parts[0];
            var domain = parts[1];
            var masked = name.Length <= 2 ? "**" : name[..1] + new string('*', Math.Min(name.Length - 2, 6)) + name[^1..];
            return $"{masked}@{domain}";
        }
        if (identifier.Length <= 4) return "****";
        return identifier[..2] + new string('*', Math.Min(identifier.Length - 4, 6)) + identifier[^2..];
    }

    private sealed record PendingChallenge(VerificationChallenge Challenge, string Identifier);
}
