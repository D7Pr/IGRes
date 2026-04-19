using Igres.Core.Auth;
using Igres.Core.Models;
using Igres.Core.Storage;
using Igres.Infrastructure.Auth;

namespace Igres.Core.Tests;

public class MockAuthCoordinatorTests
{
    private sealed class InMemorySessionStore : ISecureSessionStore
    {
        public AccountSession? Saved;
        public Task<AccountSession?> LoadSessionAsync(CancellationToken ct) => Task.FromResult(Saved);
        public Task SaveSessionAsync(AccountSession session, CancellationToken ct) { Saved = session; return Task.CompletedTask; }
        public Task ClearSessionAsync(CancellationToken ct) { Saved = null; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Correct_code_signs_in_and_persists_session()
    {
        var store = new InMemorySessionStore();
        var auth = new MockAuthCoordinator(store);
        var start = await auth.StartSignInAsync(new AuthStartRequest("user@example.com", "secret"), CancellationToken.None);
        start.Challenge.Should().NotBeNull();
        var result = await auth.SubmitVerificationCodeAsync(start.Challenge!.ChallengeId, MockAuthCoordinator.ExpectedVerificationCode, CancellationToken.None);
        result.Outcome.Should().Be(AuthOutcome.SignedIn);
        store.Saved.Should().NotBeNull();
    }

    [Fact]
    public async Task Wrong_code_returns_incorrect_and_reissues_challenge()
    {
        var auth = new MockAuthCoordinator(new InMemorySessionStore());
        var start = await auth.StartSignInAsync(new AuthStartRequest("user@example.com", "secret"), CancellationToken.None);
        var result = await auth.SubmitVerificationCodeAsync(start.Challenge!.ChallengeId, "000000", CancellationToken.None);
        result.Outcome.Should().Be(AuthOutcome.ChallengeRequired);
        result.Challenge.Should().NotBeNull();
    }

    [Fact]
    public async Task SignOut_clears_session()
    {
        var store = new InMemorySessionStore { Saved = AccountSession.SignedOut("mock") };
        var auth = new MockAuthCoordinator(store);
        await auth.SignOutAsync(CancellationToken.None);
        store.Saved.Should().BeNull();
    }
}
