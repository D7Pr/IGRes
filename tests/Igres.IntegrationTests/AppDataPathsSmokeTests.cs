using Igres.Core.Models;
using Igres.Infrastructure.Storage;

namespace Igres.IntegrationTests;

public sealed class AppDataPathsSmokeTests
{
    [Fact]
    public async Task UserPreference_service_round_trips_local_settings()
    {
        var service = new UserPreferenceService();
        await service.ClearAsync(CancellationToken.None);

        var preference = UserPreference.Default with
        {
            PageSize = 70,
            MaxConcurrency = 4,
            ShowDiagnostics = true
        };

        await service.SaveAsync(preference, CancellationToken.None);
        var reloaded = await service.LoadAsync(CancellationToken.None);

        reloaded.PageSize.Should().Be(70);
        reloaded.MaxConcurrency.Should().Be(4);
        reloaded.ShowDiagnostics.Should().BeTrue();

        await service.ClearAsync(CancellationToken.None);
    }
}
