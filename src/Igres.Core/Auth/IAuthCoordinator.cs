using Igres.Core.Models;

namespace Igres.Core.Auth;

public enum AuthOutcome
{
    SignedIn,
    ChallengeRequired,
    Failed,
    Canceled
}

public sealed record AuthStartResult(
    AuthOutcome Outcome,
    AccountSession? Session,
    VerificationChallenge? Challenge,
    string? ErrorMessage = null);

public sealed record AuthChallengeResult(
    AuthOutcome Outcome,
    AccountSession? Session,
    VerificationChallenge? Challenge,
    string? ErrorMessage = null);

public sealed record AuthStartRequest(string? Identifier = null, string? Secret = null);

public interface IAuthCoordinator
{
    Task<AuthStartResult> StartSignInAsync(AuthStartRequest request, CancellationToken cancellationToken);
    Task<AuthChallengeResult> SubmitVerificationCodeAsync(string challengeId, string code, CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
    Task<AccountSession?> RestoreSessionAsync(CancellationToken cancellationToken);
}
