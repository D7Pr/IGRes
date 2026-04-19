using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Storage;
using Igres.Infrastructure.Providers.Real;

namespace Igres.Core.Tests;

public class RealProviderCapabilityTests
{
    private sealed class FixedStore : ICapturedHeadersStore
    {
        private readonly CapturedHeaders? _headers;

        public FixedStore(CapturedHeaders? headers) => _headers = headers;

        public Task SaveAsync(CapturedHeaders headers, CancellationToken ct) => Task.CompletedTask;
        public Task<CapturedHeaders?> LoadAsync(CancellationToken ct) => Task.FromResult(_headers);
        public Task ClearAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Without_credentials_all_surfaces_are_not_listable()
    {
        using var provider = new RealAccountActivityProvider(new FixedStore(null));

        var caps = await provider.GetCapabilitiesAsync(CancellationToken.None);

        caps.Should().OnlyContain(c => c.CanList == false);
        caps.Should().OnlyContain(c => !string.IsNullOrEmpty(c.Notes));
    }

    [Fact]
    public async Task With_credentials_but_no_bloks_uses_fallback_and_enables_all_surfaces()
    {
        using var provider = new RealAccountActivityProvider(new FixedStore(CreateHeaders(bloksVersionId: string.Empty)));

        var caps = await provider.GetCapabilitiesAsync(CancellationToken.None);

        caps.Single(c => c.Surface == ActivitySurface.SavedItems).CanList.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.SavedCollections).CanList.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Likes).CanList.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Comments).CanList.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Reposts).CanList.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Likes).Notes.Should().BeNull();
    }

    [Fact]
    public async Task With_credentials_and_bloks_all_surfaces_are_listable()
    {
        using var provider = new RealAccountActivityProvider(new FixedStore(CreateHeaders(bloksVersionId: "bloks-v1")));

        var caps = await provider.GetCapabilitiesAsync(CancellationToken.None);

        caps.Should().OnlyContain(c => c.CanList);
        caps.Single(c => c.Surface == ActivitySurface.SavedItems).CanDeleteAll.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.SavedCollections).CanDeleteCollection.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Likes).CanDeleteSelected.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Comments).CanDeleteAll.Should().BeTrue();
        caps.Single(c => c.Surface == ActivitySurface.Reposts).CanDeleteAll.Should().BeTrue();
    }

    [Fact]
    public async Task Destructive_methods_throw_unsupported()
    {
        using var provider = new RealAccountActivityProvider(new FixedStore(null));

        var act = async () => await provider.RemoveSavedItemsAsync(["x"], null, CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedCapabilityException>();
    }

    [Fact]
    public async Task Saved_items_without_credentials_throws()
    {
        using var provider = new RealAccountActivityProvider(new FixedStore(null));

        var act = async () => await provider.GetSavedItemsAsync(new PageRequest(10), CancellationToken.None);

        await act.Should().ThrowAsync<UnsupportedCapabilityException>();
    }

    private static CapturedHeaders CreateHeaders(string bloksVersionId) =>
        new(
            Authorization: "Bearer IGT:2:test",
            UserAgent: "Instagram 423.1.0.30.69 Android",
            XIgAppId: "124024574287414",
            XIgDeviceId: "device-1234",
            XIgFamilyDeviceId: "family-1234",
            XMid: "mid-1234",
            IgUDsUserId: "123456789",
            IgIntendedUserId: "123456789",
            IgURur: "rur-1234",
            XBloksVersionId: bloksVersionId,
            AcceptLanguage: "en-US",
            XIgAppLocale: "en_US",
            XIgDeviceLocale: "en_US",
            XIgMappedLocale: "en_US",
            CapturedAt: DateTimeOffset.UtcNow);
}
