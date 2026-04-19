namespace Igres.Core.Models;

public enum VerificationChannel
{
    Sms,
    Email,
    Authenticator,
    Unknown
}

public sealed record VerificationChallenge(
    string ChallengeId,
    VerificationChannel Channel,
    string DestinationHint,
    DateTimeOffset? ExpiresAt,
    bool CanResend,
    bool CanRestart,
    string? ErrorMessage = null);
