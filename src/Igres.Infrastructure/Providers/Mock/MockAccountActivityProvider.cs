using System.Collections.Concurrent;
using Igres.Core.Models;
using Igres.Core.Providers;

namespace Igres.Infrastructure.Providers.Mock;

public sealed class MockAccountActivityProvider : IAccountActivityProvider
{
    private readonly Func<int> _getMaxConcurrency;
    public string ProviderName => "Mock";

    private readonly ConcurrentDictionary<string, ActivityItem> _savedItems = new();
    private readonly ConcurrentDictionary<string, ActivityItem> _likes = new();
    private readonly ConcurrentDictionary<string, ActivityItem> _comments = new();
    private readonly ConcurrentDictionary<string, ActivityItem> _reposts = new();
    private readonly ConcurrentDictionary<string, SavedCollection> _collections = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ActivityItem>> _collectionItems = new();
    private readonly int _transientFailureEvery;
    private int _operationCounter;

    public MockAccountActivityProvider(Func<int>? getMaxConcurrency = null, int transientFailureEvery = 0)
    {
        _getMaxConcurrency = getMaxConcurrency ?? (() => 3);
        _transientFailureEvery = transientFailureEvery;
        Seed();
    }

    public void Reset()
    {
        _savedItems.Clear();
        _likes.Clear();
        _comments.Clear();
        _reposts.Clear();
        _collections.Clear();
        _collectionItems.Clear();
        Interlocked.Exchange(ref _operationCounter, 0);
        Seed();
    }

    private static readonly string[] Captions =
    {
        "Morning light over the river",
        "Weekend trail, long shadows",
        "Quiet studio hours",
        "City skyline in soft focus",
        "Pages from a slow afternoon",
        "Kitchen experiments continue",
        "Late train, empty platform",
        "Rooftop summer, warm pavement",
        "Late-night code and coffee",
        "A garden finally catching up",
        "Handwritten drafts before the rewrite",
        "Out with friends, back with stories",
        "Small wins today",
        "A found poem on a receipt",
        "Testing a new lens",
        "Rain all afternoon"
    };

    private static readonly string[] Handles =
    {
        "alex.makes", "studio.nora", "river_films", "the_slow_table",
        "late_night_desk", "ronin.designs", "field_notes", "bright.lines",
        "paper.layers", "wanderandprint"
    };

    private void Seed()
    {
        var rng = new Random(17);
        for (int i = 0; i < 60; i++)
        {
            var item = CreateItem($"save-{i:000}", ActivitySurface.SavedItems, i, rng);
            _savedItems[item.Id] = item;
        }
        for (int i = 0; i < 40; i++)
        {
            var item = CreateItem($"like-{i:000}", ActivitySurface.Likes, i, rng);
            _likes[item.Id] = item;
        }
        for (int i = 0; i < 30; i++)
        {
            var item = CreateItem($"comment-{i:000}", ActivitySurface.Comments, i, rng) with { MediaKind = MediaKind.Comment };
            _comments[item.Id] = item;
        }
        for (int i = 0; i < 25; i++)
        {
            var item = CreateItem($"repost-{i:000}", ActivitySurface.Reposts, i, rng);
            _reposts[item.Id] = item;
        }
        var names = new[] { "Travel 2025", "Interiors", "Books to read", "Recipes", "Longform reads", "Inspiration" };
        for (int i = 0; i < names.Length; i++)
        {
            var supportsDelete = i != 1;
            var supportsItemRemoval = i != 2;
            var itemCount = rng.Next(4, 12);
            var previewUris = Enumerable.Range(0, Math.Min(3, itemCount))
                .Select(k => PreviewUri($"col-{i}-{k}", rng))
                .ToList();
            var collection = new SavedCollection(
                CollectionId: $"collection-{i:000}",
                Name: names[i],
                PreviewUris: previewUris,
                ItemCount: itemCount,
                LastUpdatedAt: DateTimeOffset.UtcNow.AddDays(-rng.Next(1, 45)),
                SupportsDelete: supportsDelete,
                SupportsItemRemoval: supportsItemRemoval);
            _collections[collection.CollectionId] = collection;
            var inner = new ConcurrentDictionary<string, ActivityItem>();
            for (int k = 0; k < itemCount; k++)
            {
                var childId = $"{collection.CollectionId}-item-{k:000}";
                var child = CreateItem(childId, ActivitySurface.SavedItems, k + i * 10, rng) with
                {
                    ParentCollectionId = collection.CollectionId,
                    SupportsDelete = supportsItemRemoval
                };
                inner[child.Id] = child;
            }
            _collectionItems[collection.CollectionId] = inner;
        }
    }

    private static ActivityItem CreateItem(string id, ActivitySurface surface, int index, Random rng)
    {
        var caption = Captions[index % Captions.Length];
        var handle = Handles[index % Handles.Length];
        var kind = (MediaKind)(rng.Next(0, 3));
        return new ActivityItem(
            Id: id,
            Surface: surface,
            MediaKind: kind,
            PreviewUri: PreviewUri(id, rng),
            AuthorHandle: "@" + handle,
            AuthorDisplayName: FormatDisplayName(handle),
            TextSnippet: caption,
            OccurredAt: DateTimeOffset.UtcNow.AddHours(-rng.Next(1, 24 * 90)),
            SourceUri: null,
            ParentCollectionId: null,
            SupportsSelection: true,
            SupportsDelete: true,
            Metadata: null);
    }

    private static string FormatDisplayName(string handle)
    {
        var parts = handle.Split(new[] { '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string PreviewUri(string seed, Random rng)
    {
        var hue = (seed.GetHashCode() & 0x7fffffff) % 360;
        return $"preview://hue/{hue}/{Math.Abs(rng.Next())}";
    }

    public Task<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderCapability> caps = new[]
        {
            new ProviderCapability(ActivitySurface.SavedItems, true, true, true, false, true),
            new ProviderCapability(ActivitySurface.SavedCollections, true, true, false, true, true),
            new ProviderCapability(ActivitySurface.Reposts, true, true, true, false, true),
            new ProviderCapability(ActivitySurface.Likes, true, true, true, false, true),
            new ProviderCapability(ActivitySurface.Comments, true, true, false, false, true, "Bulk comment deletion is limited to selected entries.")
        };
        return Task.FromResult(caps);
    }

    public Task<PagedResult<ActivityItem>> GetSavedItemsAsync(PageRequest request, CancellationToken cancellationToken) => Page(_savedItems.Values, request);
    public Task<PagedResult<ActivityItem>> GetLikesAsync(PageRequest request, CancellationToken cancellationToken) => Page(_likes.Values, request);
    public Task<PagedResult<ActivityItem>> GetCommentsAsync(PageRequest request, CancellationToken cancellationToken) => Page(_comments.Values, request);
    public Task<PagedResult<ActivityItem>> GetRepostsAsync(PageRequest request, CancellationToken cancellationToken) => Page(_reposts.Values, request);

    public Task<PagedResult<SavedCollection>> GetSavedCollectionsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var items = _collections.Values.OrderByDescending(c => c.LastUpdatedAt ?? DateTimeOffset.MinValue).ToList();
        return Task.FromResult(PageFrom(items, request));
    }

    public Task<PagedResult<ActivityItem>> GetCollectionItemsAsync(string collectionId, PageRequest request, CancellationToken cancellationToken)
    {
        if (_collectionItems.TryGetValue(collectionId, out var inner))
        {
            return Page(inner.Values, request);
        }
        return Task.FromResult(new PagedResult<ActivityItem>(Array.Empty<ActivityItem>(), null, false));
    }

    private Task<PagedResult<ActivityItem>> Page(IEnumerable<ActivityItem> source, PageRequest request)
    {
        var ordered = source.OrderByDescending(i => i.OccurredAt ?? DateTimeOffset.MinValue).ToList();
        return Task.FromResult(PageFrom(ordered, request));
    }

    private static PagedResult<T> PageFrom<T>(IReadOnlyList<T> ordered, PageRequest request)
    {
        var offset = 0;
        if (!string.IsNullOrEmpty(request.Cursor) && int.TryParse(request.Cursor, out var parsed))
        {
            offset = parsed;
        }
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var slice = ordered.Skip(offset).Take(pageSize).ToArray();
        var hasMore = offset + slice.Length < ordered.Count;
        var nextCursor = hasMore ? (offset + slice.Length).ToString() : null;
        return new PagedResult<T>(slice, nextCursor, hasMore);
    }

    public Task<BulkActionResult> RemoveSavedItemsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => RunRemovalAsync(_savedItems, itemIds, progress, cancellationToken);

    public async Task<BulkActionResult> RemoveAllSavedItemsAsync(IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        var ids = _savedItems.Keys.ToArray();
        return await RunRemovalAsync(_savedItems, ids, progress, cancellationToken);
    }

    public async Task<BulkActionResult> RemoveCollectionItemsAsync(string collectionId, IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        if (!_collectionItems.TryGetValue(collectionId, out var inner))
        {
            throw new UnsupportedCapabilityException($"Collection {collectionId} is not available.");
        }
        var result = await RunRemovalAsync(inner, itemIds, progress, cancellationToken);
        if (_collections.TryGetValue(collectionId, out var collection))
        {
            _collections[collectionId] = collection with { ItemCount = inner.Count };
        }
        return result;
    }

    public async Task<BulkActionResult> DeleteCollectionAsync(string collectionId, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        if (!_collections.TryGetValue(collectionId, out var collection))
        {
            var notFound = new BulkActionResultItem(collectionId, BulkActionOutcome.Failed, "Collection no longer exists.");
            progress?.Report(notFound);
            return new BulkActionResult(0, 1, 0, new[] { notFound });
        }
        if (!collection.SupportsDelete)
        {
            var skipped = new BulkActionResultItem(collectionId, BulkActionOutcome.Skipped, "Deleting this collection is not supported.");
            progress?.Report(skipped);
            return new BulkActionResult(0, 0, 1, new[] { skipped });
        }
        await Task.Delay(150, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _collections.TryRemove(collectionId, out _);
        _collectionItems.TryRemove(collectionId, out _);
        var ok = new BulkActionResultItem(collectionId, BulkActionOutcome.Succeeded, $"Deleted \"{collection.Name}\".");
        progress?.Report(ok);
        return new BulkActionResult(1, 0, 0, new[] { ok });
    }

    public Task<BulkActionResult> RemoveLikesAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => RunRemovalAsync(_likes, itemIds, progress, cancellationToken);

    public Task<BulkActionResult> DeleteCommentsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => RunRemovalAsync(_comments, itemIds, progress, cancellationToken);

    public Task<BulkActionResult> RemoveRepostsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => RunRemovalAsync(_reposts, itemIds, progress, cancellationToken);

    private async Task<BulkActionResult> RunRemovalAsync(
        ConcurrentDictionary<string, ActivityItem> store,
        IReadOnlyList<string> ids,
        IProgress<BulkActionResultItem>? progress,
        CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Clamp(_getMaxConcurrency(), 1, 10);
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var results = new ConcurrentBag<BulkActionResultItem>();
        var ok = 0;
        var fail = 0;
        var skip = 0;

        var tasks = ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(Math.Max(8, 35 - ((maxConcurrency - 1) * 3)), cancellationToken);
                BulkActionResultItem outcome;
                if (!store.ContainsKey(id))
                {
                    outcome = new BulkActionResultItem(id, BulkActionOutcome.Skipped, "Item is no longer available.");
                    Interlocked.Increment(ref skip);
                }
                else if (_transientFailureEvery > 0 && Interlocked.Increment(ref _operationCounter) % _transientFailureEvery == 0)
                {
                    outcome = new BulkActionResultItem(id, BulkActionOutcome.Failed, "Simulated transient error.");
                    Interlocked.Increment(ref fail);
                }
                else
                {
                    store.TryRemove(id, out _);
                    outcome = new BulkActionResultItem(id, BulkActionOutcome.Succeeded);
                    Interlocked.Increment(ref ok);
                }

                results.Add(outcome);
                progress?.Report(outcome);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return new BulkActionResult(ok, fail, skip, results.ToArray());
    }
}
