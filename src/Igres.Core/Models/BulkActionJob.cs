namespace Igres.Core.Models;

public enum BulkActionType
{
    RemoveSelected,
    RemoveAll,
    DeleteCollection,
    RemoveCollectionItems
}

public enum BulkActionJobStatus
{
    Queued,
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed,
    Canceled
}

public enum BulkActionOutcome
{
    Succeeded,
    Failed,
    Skipped,
    Canceled
}

public sealed record BulkActionRequest(
    string JobId,
    BulkActionType ActionType,
    ActivitySurface Surface,
    IReadOnlyList<string> TargetIds,
    DateTimeOffset RequestedAt,
    bool RequiresStrongConfirmation,
    string? CollectionId = null,
    string? ActionLabel = null);

public sealed record BulkActionResultItem(
    string TargetId,
    BulkActionOutcome Outcome,
    string? Message = null);

public sealed record BulkActionResult(
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<BulkActionResultItem> Results);

public sealed record BulkActionJobSnapshot(
    string JobId,
    string ActionLabel,
    ActivitySurface Surface,
    BulkActionJobStatus Status,
    int TotalCount,
    int ProcessedCount,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    int CanceledCount,
    bool CanCancel,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<BulkActionResultItem> RecentResults,
    // Full set of target ids that succeeded so far. The view-model uses
    // this to drop deleted entries from its list — RecentResults is capped
    // at ~25 entries for the progress UI, so it cannot be relied on for
    // cleanup at scale (200+ items).
    IReadOnlyCollection<string> SucceededTargetIds);
