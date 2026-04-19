using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Infrastructure.Providers.Mock;

namespace Igres.Core.Tests;

public class MockAccountActivityProviderTests
{
    private readonly MockAccountActivityProvider _provider = new();

    [Fact]
    public async Task Reports_capability_for_every_surface()
    {
        var caps = await _provider.GetCapabilitiesAsync(CancellationToken.None);
        caps.Select(c => c.Surface).Should().BeEquivalentTo(new[]
        {
            ActivitySurface.SavedItems,
            ActivitySurface.SavedCollections,
            ActivitySurface.Reposts,
            ActivitySurface.Likes,
            ActivitySurface.Comments
        });
    }

    [Fact]
    public async Task Comments_surface_cannot_delete_all()
    {
        var caps = await _provider.GetCapabilitiesAsync(CancellationToken.None);
        var comments = caps.Single(c => c.Surface == ActivitySurface.Comments);
        comments.CanDeleteAll.Should().BeFalse();
        comments.Notes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Saved_items_are_seeded_and_pageable()
    {
        var page1 = await _provider.GetSavedItemsAsync(new PageRequest(25, null), CancellationToken.None);
        page1.Items.Count.Should().Be(25);
        page1.NextCursor.Should().NotBeNull();
        var page2 = await _provider.GetSavedItemsAsync(new PageRequest(25, page1.NextCursor), CancellationToken.None);
        page2.Items.Count.Should().BeGreaterThan(0);
        page2.Items.Select(i => i.Id).Should().NotIntersectWith(page1.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Bulk_remove_saved_items_aggregates_results()
    {
        var page = await _provider.GetSavedItemsAsync(new PageRequest(50), CancellationToken.None);
        var ids = page.Items.Select(i => i.Id).Take(30).ToList();
        var result = await _provider.RemoveSavedItemsAsync(ids, null, CancellationToken.None);
        (result.SucceededCount + result.FailedCount + result.SkippedCount).Should().Be(30);
        result.Results.Should().HaveCount(30);
    }

    [Fact]
    public async Task Progress_is_reported_per_item()
    {
        var page = await _provider.GetSavedItemsAsync(new PageRequest(10), CancellationToken.None);
        var ids = page.Items.Select(i => i.Id).ToList();
        var reported = new List<BulkActionResultItem>();
        var progress = new Progress<BulkActionResultItem>(r => reported.Add(r));
        await _provider.RemoveSavedItemsAsync(ids, progress, CancellationToken.None);
        await Task.Delay(50);
        reported.Count.Should().BeGreaterThan(0);
    }
}
