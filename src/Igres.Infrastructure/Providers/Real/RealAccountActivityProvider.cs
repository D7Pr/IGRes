using System.Globalization;
using System.Text.Json;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Storage;

namespace Igres.Infrastructure.Providers.Real;

/// <summary>
/// Live adapter that replays a captured mobile session against Instagram's private API.
/// Saved items use direct feed/collection endpoints, while likes/comments/reposts use the
/// Bloks-backed activity-center flows captured in the request log.
/// </summary>
public sealed class RealAccountActivityProvider : IAccountActivityProvider, IDisposable
{
    private const string SavedItemsPath =
        "/api/v1/feed/saved/all/?clips_subtab_first=true&include_clips_subtab=true&include_collection_info=1&include_igtv_tab=true&module_name=feed_saved_collections";

    private const string SavedCollectionsModuleName = "feed_saved_collections";

    private const string DefaultBkClientContext =
        """{"theme_params":[{"design_system_name":"XMDS","value":["three_neutral_gray"]}],"styles_id":"instagram","pixel_ratio":3}""";

    private readonly ICapturedHeadersStore _store;
    private readonly Func<int> _getMaxConcurrency;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private InstagramSessionClient? _client;
    private CapturedHeaders? _headers;

    public RealAccountActivityProvider(ICapturedHeadersStore store, Func<int>? getMaxConcurrency = null)
    {
        _store = store;
        _getMaxConcurrency = getMaxConcurrency ?? (() => 3);
    }

    public string ProviderName => "Instagram (captured session)";

    public async Task<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var hasSession = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var hasBloks = hasSession && HasBloksVersion();
        var noSessionMessage = "Paste a captured mobile request in Settings to enable the live adapter.";
        var noBloksMessage = hasSession
            ? "This session is missing X-Bloks-Version-Id, so Instagram activity-center surfaces cannot be loaded yet."
            : noSessionMessage;

        IReadOnlyList<ProviderCapability> caps = new[]
        {
            new ProviderCapability(ActivitySurface.SavedItems, hasSession, hasSession, hasSession, false, true, hasSession ? null : noSessionMessage),
            new ProviderCapability(ActivitySurface.SavedCollections, hasSession, hasSession, false, hasSession, true,
                hasSession ? "Collection names are resolved from saved-item metadata when Instagram includes them." : noSessionMessage),
            new ProviderCapability(ActivitySurface.Reposts, hasBloks, hasBloks, hasBloks, false, true, hasBloks ? null : noBloksMessage),
            new ProviderCapability(ActivitySurface.Likes, hasBloks, hasBloks, hasBloks, false, true, hasBloks ? null : noBloksMessage),
            new ProviderCapability(ActivitySurface.Comments, hasBloks, hasBloks, hasBloks, false, true, hasBloks ? null : noBloksMessage),
        };
        return caps;
    }

    public async Task<PagedResult<ActivityItem>> GetSavedItemsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var client = await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        var path = SavedItemsPath;
        if (!string.IsNullOrEmpty(request.Cursor))
            path += "&max_id=" + Uri.EscapeDataString(request.Cursor);

        var body = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return ParseSavedFeed(body);
    }

    public async Task<PagedResult<SavedCollection>> GetSavedCollectionsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        var items = await GetAllSavedItemsAsync(cancellationToken).ConfigureAwait(false);
        var collections = BuildSavedCollections(items);
        return PageInMemory(collections, request);
    }

    public async Task<PagedResult<ActivityItem>> GetCollectionItemsAsync(string collectionId, PageRequest request, CancellationToken cancellationToken)
    {
        await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        var items = await GetAllSavedItemsAsync(cancellationToken).ConfigureAwait(false);
        var filtered = items
            .Where(item => ItemBelongsToCollection(item, collectionId))
            .Select(item => item with { ParentCollectionId = collectionId, SupportsDelete = true })
            .ToList();
        return PageInMemory(filtered, request);
    }

    public async Task<PagedResult<ActivityItem>> GetLikesAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var data = await LoadBloksSurfaceAsync(ActivitySurface.Likes, cancellationToken).ConfigureAwait(false);
        return PageInMemory(data.Items, request);
    }

    public async Task<PagedResult<ActivityItem>> GetCommentsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var data = await LoadBloksSurfaceAsync(ActivitySurface.Comments, cancellationToken).ConfigureAwait(false);
        return PageInMemory(data.Items, request);
    }

    public async Task<PagedResult<ActivityItem>> GetRepostsAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var data = await LoadBloksSurfaceAsync(ActivitySurface.Reposts, cancellationToken).ConfigureAwait(false);
        return PageInMemory(data.Items, request);
    }

    public Task<BulkActionResult> RemoveSavedItemsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken) =>
        ExecuteCollectionBulkRemoveAsync(itemIds, progress, cancellationToken);

    public async Task<BulkActionResult> RemoveAllSavedItemsAsync(IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        var items = await GetAllSavedItemsAsync(cancellationToken).ConfigureAwait(false);
        var ids = items.Select(i => i.Id).ToArray();
        return await ExecuteCollectionBulkRemoveAsync(ids, progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<BulkActionResult> RemoveCollectionItemsAsync(string collectionId, IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken) =>
        ExecuteCollectionBulkRemoveAsync(itemIds, progress, cancellationToken);

    public async Task<BulkActionResult> DeleteCollectionAsync(string collectionId, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
            return FailedSingle("<collection>", "Collection id is missing.", progress);

        var client = await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        var payload = new Dictionary<string, object?>
        {
            ["_uuid"] = CurrentDeviceId(),
            ["_uid"] = CurrentUserId(),
            ["module_name"] = SavedCollectionsModuleName,
        };

        try
        {
            await client.PostSignedBodyAsync($"/api/v1/collections/{collectionId}/delete/", payload, cancellationToken).ConfigureAwait(false);
            return SuccessSingle(collectionId, progress, "Collection deleted.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailedSingle(collectionId, ex.Message, progress);
        }
    }

    public async Task<BulkActionResult> RemoveLikesAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        var data = await LoadBloksSurfaceAsync(ActivitySurface.Likes, cancellationToken).ConfigureAwait(false);
        return await ExecuteBloksBulkDeleteAsync(itemIds, data.DeleteContext, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkActionResult> DeleteCommentsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        var data = await LoadBloksSurfaceAsync(ActivitySurface.Comments, cancellationToken).ConfigureAwait(false);
        return await ExecuteBloksBulkDeleteAsync(itemIds, data.DeleteContext, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkActionResult> RemoveRepostsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        var data = await LoadBloksSurfaceAsync(ActivitySurface.Reposts, cancellationToken).ConfigureAwait(false);
        return await ExecuteBloksBulkDeleteAsync(itemIds, data.DeleteContext, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BloksSurfaceData> LoadBloksSurfaceAsync(ActivitySurface surface, CancellationToken cancellationToken)
    {
        var client = await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        RequireBloksVersion();

        var path = surface switch
        {
            ActivitySurface.Likes => "/api/v1/bloks/apps/com.instagram.privacy.activity_center.liked_media_screen/",
            ActivitySurface.Comments => "/api/v1/bloks/apps/com.instagram.privacy.activity_center.comments_screen/",
            ActivitySurface.Reposts => "/api/v1/bloks/apps/com.instagram.privacy.activity_center.media_repost_screen/",
            _ => throw new UnsupportedCapabilityException($"{surface} is not backed by a Bloks activity-center screen.")
        };

        var payload = new Dictionary<string, object?>
        {
            ["_uuid"] = CurrentDeviceId(),
            ["_uid"] = CurrentUserId(),
            ["bloks_versioning_id"] = CurrentBloksVersionId(),
            ["bk_client_context"] = DefaultBkClientContext,
        };

        var body = await client.PostSignedBodyAsync(path, payload, cancellationToken).ConfigureAwait(false);
        var referenceTime = DateTimeOffset.UtcNow;
        return surface switch
        {
            ActivitySurface.Likes => BloksScreenParser.ParseLikes(body, referenceTime),
            ActivitySurface.Comments => BloksScreenParser.ParseComments(body, referenceTime),
            ActivitySurface.Reposts => BloksScreenParser.ParseReposts(body, referenceTime),
            _ => throw new UnsupportedCapabilityException($"{surface} is not backed by a Bloks activity-center screen.")
        };
    }

    private static void DumpPayload(ActivitySurface surface, string body)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "user-data");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"dump-{surface}.txt"), body);
        }
        catch
        {
            // Diagnostic helper only — never fail the request.
        }
    }

    private async Task<BulkActionResult> ExecuteCollectionBulkRemoveAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
    {
        var normalized = itemIds
            .Select(NormalizeSavedItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            return EmptyResult();

        var client = await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        return await RunBatchActionAsync(
            normalized,
            batch => client.PostFormAsync("/api/v1/collections/bulk_remove/", new Dictionary<string, string>
            {
                ["media_ids"] = JsonSerializer.Serialize(batch),
                ["_uuid"] = CurrentDeviceId(),
            }, cancellationToken),
            progress,
            batchSize: 50,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<BulkActionResult> ExecuteBloksBulkDeleteAsync(
        IReadOnlyList<string> itemIds,
        BloksDeleteContext context,
        IProgress<BulkActionResultItem>? progress,
        CancellationToken cancellationToken)
    {
        var targets = itemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (targets.Length == 0)
            return EmptyResult();

        var client = await RequireClientAsync(cancellationToken).ConfigureAwait(false);
        return await RunBatchActionAsync(
            targets,
            batch => client.PostSignedBodyAsync(context.EndpointPath, BuildBloksDeletePayload(context, batch), cancellationToken),
            progress,
            batchSize: 100,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<BulkActionResult> RunBatchActionAsync(
        IReadOnlyList<string> ids,
        Func<IReadOnlyList<string>, Task<string>> sendBatchAsync,
        IProgress<BulkActionResultItem>? progress,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<BulkActionResultItem>();
        var succeeded = 0;
        var failed = 0;
        var batches = ids
            .Select((id, index) => new { id, index })
            .GroupBy(x => x.index / batchSize)
            .Select(group => group.Select(x => x.id).ToArray())
            .ToArray();

        using var gate = new SemaphoreSlim(Math.Clamp(_getMaxConcurrency(), 1, 10));
        var tasks = batches.Select(async batch =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sendBatchAsync(batch).ConfigureAwait(false);
                foreach (var id in batch)
                {
                    var result = new BulkActionResultItem(id, BulkActionOutcome.Succeeded);
                    results.Add(result);
                    progress?.Report(result);
                    Interlocked.Increment(ref succeeded);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var id in batch)
                {
                    var result = new BulkActionResultItem(id, BulkActionOutcome.Failed, ex.Message);
                    results.Add(result);
                    progress?.Report(result);
                    Interlocked.Increment(ref failed);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return new BulkActionResult(succeeded, failed, 0, results.ToArray());
    }

    private IReadOnlyDictionary<string, object?> BuildBloksDeletePayload(BloksDeleteContext context, IReadOnlyList<string> ids) =>
        new Dictionary<string, object?>
        {
            ["main_content_type_value"] = 0,
            ["main_filter_to_visible_on_facebook_value"] = 0,
            ["shared_user_id"] = string.Empty,
            ["_uuid"] = CurrentDeviceId(),
            ["main_date_start_state_value"] = -1,
            ["_uid"] = CurrentUserId(),
            ["main_account_history_events_state_value"] = string.Empty,
            ["main_date_end_state_value"] = -1,
            ["main_filter_to_visible_from_facebook_value"] = 0,
            ["main_content_types_value"] = "Posts, Reels",
            ["items_for_action"] = string.Join(",", ids),
            ["main_authors_state_value"] = string.Empty,
            ["bk_client_context"] = DefaultBkClientContext,
            ["main_includes_location_value"] = 0,
            ["content_element_id"] = context.ContentElementId,
            ["content_spinner_id"] = context.ContentSpinnerId,
            ["main_order_state_value"] = 1,
            ["main_liked_privately_value"] = 0,
            ["bloks_versioning_id"] = CurrentBloksVersionId(),
            ["entrypoint"] = string.Empty,
            ["number_of_items"] = ids.Count,
            ["main_attribute_order_state_value"] = "newest_to_oldest",
            ["content_container_id"] = context.ContentContainerId,
        };

    private async Task<IReadOnlyList<ActivityItem>> GetAllSavedItemsAsync(CancellationToken cancellationToken)
    {
        var items = new List<ActivityItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        while (true)
        {
            var page = await GetSavedItemsAsync(new PageRequest(100, cursor), cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                if (seen.Add(item.Id))
                    items.Add(item);
            }

            if (!page.HasMore || string.IsNullOrEmpty(page.NextCursor))
                break;

            cursor = page.NextCursor;
        }

        return items;
    }

    private static PagedResult<ActivityItem> ParseSavedFeed(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("save_media_response", out var saveResp))
            return new PagedResult<ActivityItem>(Array.Empty<ActivityItem>(), null, false);

        var items = new List<ActivityItem>();
        if (saveResp.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in itemsEl.EnumerateArray())
            {
                if (!entry.TryGetProperty("media", out var media))
                    continue;

                var mapped = MapMedia(media);
                if (mapped is not null)
                    items.Add(mapped);
            }
        }

        var moreAvailable = saveResp.TryGetProperty("more_available", out var more) && more.ValueKind == JsonValueKind.True;
        string? nextCursor = null;
        if (saveResp.TryGetProperty("next_max_id", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            nextCursor = nextEl.GetString();

        return new PagedResult<ActivityItem>(items, nextCursor, moreAvailable);
    }

    private static ActivityItem? MapMedia(JsonElement media)
    {
        var rawId = media.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var pk = media.TryGetProperty("pk", out var pkEl) && pkEl.TryGetInt64(out var parsedPk)
            ? parsedPk.ToString(CultureInfo.InvariantCulture)
            : rawId?.Split('_', 2)[0];
        if (string.IsNullOrWhiteSpace(pk))
            return null;

        var code = media.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
        var mediaType = media.TryGetProperty("media_type", out var mediaTypeEl) && mediaTypeEl.TryGetInt32(out var parsedMediaType)
            ? parsedMediaType
            : 1;
        var productType = media.TryGetProperty("product_type", out var productTypeEl) ? productTypeEl.GetString() : null;
        var kind = mediaType switch
        {
            2 => MediaKind.Video,
            8 => MediaKind.Carousel,
            _ => MediaKind.Image
        };

        string? caption = null;
        if (media.TryGetProperty("caption", out var captionEl)
            && captionEl.ValueKind == JsonValueKind.Object
            && captionEl.TryGetProperty("text", out var captionText))
        {
            caption = captionText.GetString();
        }

        string? handle = null;
        string? display = null;
        if (media.TryGetProperty("user", out var userEl) && userEl.ValueKind == JsonValueKind.Object)
        {
            if (userEl.TryGetProperty("username", out var usernameEl))
                handle = usernameEl.GetString();
            if (userEl.TryGetProperty("full_name", out var fullNameEl))
                display = fullNameEl.GetString();
        }

        string? previewUri = null;
        if (media.TryGetProperty("image_versions2", out var imageVersions)
            && imageVersions.ValueKind == JsonValueKind.Object
            && imageVersions.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("url", out var urlEl))
                previewUri = urlEl.GetString();
        }

        DateTimeOffset? takenAt = null;
        if (media.TryGetProperty("taken_at", out var takenAtEl) && takenAtEl.TryGetInt64(out var unix))
            takenAt = DateTimeOffset.FromUnixTimeSeconds(unix);

        var isReel = string.Equals(productType, "clips", StringComparison.OrdinalIgnoreCase);
        var sourceUri = string.IsNullOrWhiteSpace(code)
            ? null
            : $"https://www.instagram.com/{(isReel ? "reel" : "p")}/{code}/";

        var hasCollectionIds = TryGetCollectionIds(media, out var collectionIds);
        var hasCollectionNames = TryGetCollectionNames(media, out var collectionNames);
        Dictionary<string, string>? metadata = null;
        if (productType is not null || code is not null || hasCollectionIds || hasCollectionNames)
        {
            metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(productType))
                metadata["product_type"] = productType;
            if (!string.IsNullOrWhiteSpace(code))
                metadata["media_code"] = code;
            if (hasCollectionIds)
                metadata["saved_collection_ids"] = string.Join(",", collectionIds);
            if (hasCollectionNames)
                metadata["saved_collection_names"] = string.Join("|", collectionNames.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        return new ActivityItem(
            Id: pk,
            Surface: ActivitySurface.SavedItems,
            MediaKind: kind,
            PreviewUri: previewUri,
            AuthorHandle: handle,
            AuthorDisplayName: display,
            TextSnippet: caption,
            OccurredAt: takenAt,
            SourceUri: sourceUri,
            ParentCollectionId: null,
            SupportsSelection: true,
            SupportsDelete: true,
            Metadata: metadata);
    }

    private static bool TryGetCollectionIds(JsonElement media, out List<string> ids)
    {
        ids = new List<string>();
        if (!media.TryGetProperty("saved_collection_ids", out var collections) || collections.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var entry in collections.EnumerateArray())
        {
            switch (entry.ValueKind)
            {
                case JsonValueKind.String:
                    if (!string.IsNullOrWhiteSpace(entry.GetString()))
                        ids.Add(entry.GetString()!);
                    break;
                case JsonValueKind.Number:
                    if (entry.TryGetInt64(out var number))
                        ids.Add(number.ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }

        return ids.Count > 0;
    }

    private static bool TryGetCollectionNames(JsonElement media, out Dictionary<string, string> names)
    {
        names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!media.TryGetProperty("collection_info", out var collectionInfo) || collectionInfo.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var entry in collectionInfo.EnumerateArray())
        {
            string? id = null;
            string? name = null;

            if (entry.TryGetProperty("collection_id", out var idEl))
                id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString(CultureInfo.InvariantCulture) : idEl.GetString();
            if (entry.TryGetProperty("collection_name", out var nameEl))
                name = nameEl.GetString();

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                names[id] = name;
        }

        return names.Count > 0;
    }

    private static IReadOnlyList<SavedCollection> BuildSavedCollections(IReadOnlyList<ActivityItem> items)
    {
        var byId = new Dictionary<string, CollectionAccumulator>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            foreach (var collectionId in GetCollectionIds(item))
            {
                if (!byId.TryGetValue(collectionId, out var acc))
                {
                    acc = new CollectionAccumulator(collectionId, byId.Count + 1);
                    byId[collectionId] = acc;
                }

                acc.ItemCount++;
                if (item.OccurredAt is { } occurredAt && (!acc.LastUpdatedAt.HasValue || occurredAt > acc.LastUpdatedAt))
                    acc.LastUpdatedAt = occurredAt;
                if (!string.IsNullOrWhiteSpace(item.PreviewUri) && !acc.PreviewUris.Contains(item.PreviewUri))
                    acc.PreviewUris.Add(item.PreviewUri!);
                if (string.IsNullOrWhiteSpace(acc.Name))
                    acc.Name = TryResolveCollectionName(item, collectionId);
            }
        }

        return byId.Values
            .OrderByDescending(acc => acc.LastUpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(acc => acc.CollectionId, StringComparer.Ordinal)
            .Select(acc => new SavedCollection(
                CollectionId: acc.CollectionId,
                Name: string.IsNullOrWhiteSpace(acc.Name) ? $"Saved collection {acc.Ordinal}" : acc.Name,
                PreviewUris: acc.PreviewUris.Take(3).ToArray(),
                ItemCount: acc.ItemCount,
                LastUpdatedAt: acc.LastUpdatedAt,
                SupportsDelete: true,
                SupportsItemRemoval: true))
            .ToArray();
    }

    private static IEnumerable<string> GetCollectionIds(ActivityItem item)
    {
        if (item.Metadata is null || !item.Metadata.TryGetValue("saved_collection_ids", out var raw) || string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool ItemBelongsToCollection(ActivityItem item, string collectionId) =>
        GetCollectionIds(item).Any(id => string.Equals(id, collectionId, StringComparison.Ordinal));

    private static string? TryResolveCollectionName(ActivityItem item, string collectionId)
    {
        if (item.Metadata is null || !item.Metadata.TryGetValue("saved_collection_names", out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        foreach (var pair in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!string.Equals(pair[..separator], collectionId, StringComparison.Ordinal))
                continue;

            var name = pair[(separator + 1)..];
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        return null;
    }

    private static string NormalizeSavedItemId(string id) =>
        id.Split('_', 2)[0];

    private static PagedResult<T> PageInMemory<T>(IReadOnlyList<T> items, PageRequest request)
    {
        var offset = 0;
        if (!string.IsNullOrWhiteSpace(request.Cursor))
            int.TryParse(request.Cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);

        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var slice = items.Skip(offset).Take(pageSize).ToArray();
        var hasMore = offset + slice.Length < items.Count;
        var nextCursor = hasMore ? (offset + slice.Length).ToString(CultureInfo.InvariantCulture) : null;
        return new PagedResult<T>(slice, nextCursor, hasMore);
    }

    private async Task<InstagramSessionClient> RequireClientAsync(CancellationToken cancellationToken)
    {
        var hasSession = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        if (!hasSession || _client is null)
            throw new UnsupportedCapabilityException("No captured session. Paste a captured mobile request in Settings.");

        return _client;
    }

    private async Task<bool> EnsureClientAsync(CancellationToken ct)
    {
        if (_client is not null)
            return true;

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null)
                return true;

            var creds = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (creds is null)
                return false;

            _headers = creds;
            _client = new InstagramSessionClient(creds);
            return true;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private void RequireBloksVersion()
    {
        if (!HasBloksVersion())
        {
            throw new UnsupportedCapabilityException(
                "This session is missing X-Bloks-Version-Id, so Instagram activity-center surfaces cannot be loaded yet.");
        }
    }

    private bool HasBloksVersion() =>
        _headers is not null
        && !string.IsNullOrWhiteSpace(CurrentBloksVersionId());

    private string CurrentBloksVersionId() =>
        InstagramSessionDefaults.ResolveBloksVersionId(_headers?.XBloksVersionId);

    private string CurrentUserId() =>
        _headers?.IgUDsUserId
        ?? _headers?.IgIntendedUserId
        ?? string.Empty;

    private string CurrentDeviceId() =>
        _headers?.XIgDeviceId
        ?? string.Empty;

    private static BulkActionResult EmptyResult() =>
        new(0, 0, 0, Array.Empty<BulkActionResultItem>());

    private static BulkActionResult SuccessSingle(string id, IProgress<BulkActionResultItem>? progress, string? message = null)
    {
        var result = new BulkActionResultItem(id, BulkActionOutcome.Succeeded, message);
        progress?.Report(result);
        return new BulkActionResult(1, 0, 0, new[] { result });
    }

    private static BulkActionResult FailedSingle(string id, string? message, IProgress<BulkActionResultItem>? progress)
    {
        var result = new BulkActionResultItem(id, BulkActionOutcome.Failed, message);
        progress?.Report(result);
        return new BulkActionResult(0, 1, 0, new[] { result });
    }

    /// <summary>Called after the user saves or clears credentials so the next call re-reads the store.</summary>
    public void InvalidateClient()
    {
        _client?.Dispose();
        _client = null;
        _headers = null;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _clientLock.Dispose();
    }

    private sealed class CollectionAccumulator
    {
        public CollectionAccumulator(string collectionId, int ordinal)
        {
            CollectionId = collectionId;
            Ordinal = ordinal;
        }

        public string CollectionId { get; }
        public int Ordinal { get; }
        public string? Name { get; set; }
        public List<string> PreviewUris { get; } = new();
        public int ItemCount { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }
}
