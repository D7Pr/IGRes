using Igres.Core.BulkActions;
using Igres.Core.Models;

namespace Igres.Core.Services;

public sealed class CollectionCleanupService
{
    private readonly IBulkActionJobRunner _runner;

    public CollectionCleanupService(IBulkActionJobRunner runner)
    {
        _runner = runner;
    }

    public Task<string> RemoveSelectedItemsAsync(string collectionId, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
    {
        var request = new BulkActionRequest(
            JobId: Guid.NewGuid().ToString("N"),
            ActionType: BulkActionType.RemoveCollectionItems,
            Surface: ActivitySurface.SavedCollections,
            TargetIds: itemIds,
            RequestedAt: DateTimeOffset.UtcNow,
            RequiresStrongConfirmation: false,
            CollectionId: collectionId,
            ActionLabel: "Remove items from collection");
        return _runner.QueueAsync(request, cancellationToken);
    }

    public Task<string> DeleteCollectionAsync(SavedCollection collection, CancellationToken cancellationToken)
    {
        var request = new BulkActionRequest(
            JobId: Guid.NewGuid().ToString("N"),
            ActionType: BulkActionType.DeleteCollection,
            Surface: ActivitySurface.SavedCollections,
            TargetIds: new[] { collection.CollectionId },
            RequestedAt: DateTimeOffset.UtcNow,
            RequiresStrongConfirmation: true,
            CollectionId: collection.CollectionId,
            ActionLabel: $"Delete collection \"{collection.Name}\"");
        return _runner.QueueAsync(request, cancellationToken);
    }
}
