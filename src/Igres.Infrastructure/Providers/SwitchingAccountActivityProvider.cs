using Igres.Core.Models;
using Igres.Core.Providers;

namespace Igres.Infrastructure.Providers;

/// <summary>
/// Delegates to either the mock provider or the real (captured-session) provider based on a
/// runtime flag. The desktop composition root flips the flag when the user toggles
/// <c>UseCapturedCredentials</c> in Settings or saves/clears captured headers.
/// </summary>
public sealed class SwitchingAccountActivityProvider : IAccountActivityProvider, IDisposable
{
    private readonly IAccountActivityProvider _mock;
    private readonly IAccountActivityProvider _real;
    private volatile bool _useReal;

    public SwitchingAccountActivityProvider(IAccountActivityProvider mock, IAccountActivityProvider real)
    {
        _mock = mock;
        _real = real;
    }

    public void UseReal(bool value) => _useReal = value;

    private IAccountActivityProvider Current => _useReal ? _real : _mock;

    public string ProviderName => Current.ProviderName;

    public Task<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken)
        => Current.GetCapabilitiesAsync(cancellationToken);

    public Task<PagedResult<ActivityItem>> GetSavedItemsAsync(PageRequest request, CancellationToken cancellationToken)
        => Current.GetSavedItemsAsync(request, cancellationToken);
    public Task<PagedResult<SavedCollection>> GetSavedCollectionsAsync(PageRequest request, CancellationToken cancellationToken)
        => Current.GetSavedCollectionsAsync(request, cancellationToken);
    public Task<PagedResult<ActivityItem>> GetCollectionItemsAsync(string collectionId, PageRequest request, CancellationToken cancellationToken)
        => Current.GetCollectionItemsAsync(collectionId, request, cancellationToken);
    public Task<PagedResult<ActivityItem>> GetLikesAsync(PageRequest request, CancellationToken cancellationToken)
        => Current.GetLikesAsync(request, cancellationToken);
    public Task<PagedResult<ActivityItem>> GetCommentsAsync(PageRequest request, CancellationToken cancellationToken)
        => Current.GetCommentsAsync(request, cancellationToken);
    public Task<PagedResult<ActivityItem>> GetRepostsAsync(PageRequest request, CancellationToken cancellationToken)
        => Current.GetRepostsAsync(request, cancellationToken);

    public Task<BulkActionResult> RemoveSavedItemsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.RemoveSavedItemsAsync(itemIds, progress, cancellationToken);
    public Task<BulkActionResult> RemoveAllSavedItemsAsync(IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.RemoveAllSavedItemsAsync(progress, cancellationToken);
    public Task<BulkActionResult> RemoveCollectionItemsAsync(string collectionId, IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.RemoveCollectionItemsAsync(collectionId, itemIds, progress, cancellationToken);
    public Task<BulkActionResult> DeleteCollectionAsync(string collectionId, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.DeleteCollectionAsync(collectionId, progress, cancellationToken);
    public Task<BulkActionResult> RemoveLikesAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.RemoveLikesAsync(itemIds, progress, cancellationToken);
    public Task<BulkActionResult> DeleteCommentsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.DeleteCommentsAsync(itemIds, progress, cancellationToken);
    public Task<BulkActionResult> RemoveRepostsAsync(IReadOnlyList<string> itemIds, IProgress<BulkActionResultItem>? progress, CancellationToken cancellationToken)
        => Current.RemoveRepostsAsync(itemIds, progress, cancellationToken);

    public void Dispose()
    {
        if (_mock is IDisposable mock)
            mock.Dispose();

        if (_real is IDisposable real)
            real.Dispose();
    }
}
