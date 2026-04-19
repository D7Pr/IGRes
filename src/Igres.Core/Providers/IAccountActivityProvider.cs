using Igres.Core.Models;

namespace Igres.Core.Providers;

public interface IAccountActivityProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken);

    Task<PagedResult<ActivityItem>> GetSavedItemsAsync(PageRequest request, CancellationToken cancellationToken);
    Task<PagedResult<SavedCollection>> GetSavedCollectionsAsync(PageRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ActivityItem>> GetCollectionItemsAsync(string collectionId, PageRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ActivityItem>> GetLikesAsync(PageRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ActivityItem>> GetCommentsAsync(PageRequest request, CancellationToken cancellationToken);
    Task<PagedResult<ActivityItem>> GetRepostsAsync(PageRequest request, CancellationToken cancellationToken);

    Task<BulkActionResult> RemoveSavedItemsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
    Task<BulkActionResult> RemoveAllSavedItemsAsync(IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
    Task<BulkActionResult> RemoveCollectionItemsAsync(string collectionId, IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
    Task<BulkActionResult> DeleteCollectionAsync(string collectionId, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
    Task<BulkActionResult> RemoveLikesAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
    Task<BulkActionResult> DeleteCommentsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
    Task<BulkActionResult> RemoveRepostsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken);
}

public sealed class UnsupportedCapabilityException : Exception
{
    public UnsupportedCapabilityException(string message) : base(message) { }
}
