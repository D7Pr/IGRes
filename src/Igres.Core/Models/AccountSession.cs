namespace Igres.Core.Models;

public enum AccountSessionState
{
    SignedOut,
    SigningIn,
    AwaitingVerification,
    SignedIn,
    Expired,
    Error
}

public sealed record AccountSession(
    string AccountId,
    string DisplayName,
    string Handle,
    AccountSessionState State,
    DateTimeOffset? LastAuthenticatedAt,
    bool HasPersistentCredentials,
    string ProviderName)
{
    public static AccountSession SignedOut(string providerName) =>
        new(string.Empty, string.Empty, string.Empty, AccountSessionState.SignedOut, null, false, providerName);
}
