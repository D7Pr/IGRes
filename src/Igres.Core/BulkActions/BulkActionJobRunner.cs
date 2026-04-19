using System.Collections.Concurrent;
using Igres.Core.Logging;
using Igres.Core.Models;
using Igres.Core.Providers;

namespace Igres.Core.BulkActions;

public sealed class BulkActionJobRunner : IBulkActionJobRunner, IDisposable
{
    private readonly IAccountActivityProvider _provider;
    private readonly IRedactionService _redaction;
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();
    private readonly List<BulkActionJobSnapshot> _history = new();
    private readonly Lock _historyLock = new();
    private bool _disposed;

    public event EventHandler<BulkActionJobSnapshot>? JobUpdated;

    public BulkActionJobRunner(IAccountActivityProvider provider, IRedactionService redaction)
    {
        _provider = provider;
        _redaction = redaction;
    }

    public Task<string> QueueAsync(BulkActionRequest request, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var totalCount = request.ActionType == BulkActionType.DeleteCollection
            ? 1
            : request.TargetIds.Count;
        var label = request.ActionLabel ?? ActionLabel(request);
        var snapshot = new BulkActionJobSnapshot(
            request.JobId, label, request.Surface, BulkActionJobStatus.Queued,
            totalCount, 0, 0, 0, 0, 0,
            CanCancel: true, StartedAt: null, CompletedAt: null,
            RecentResults: Array.Empty<BulkActionResultItem>(),
            SucceededTargetIds: Array.Empty<string>());
        var entry = new JobEntry(request, cts, snapshot);
        _jobs[request.JobId] = entry;
        EmitSnapshot(entry);
        _ = Task.Run(() => RunAsync(entry), cts.Token);
        return Task.FromResult(request.JobId);
    }

    public Task CancelAsync(string jobId, CancellationToken cancellationToken)
    {
        if (_jobs.TryGetValue(jobId, out var entry))
        {
            entry.Cts.Cancel();
        }
        return Task.CompletedTask;
    }

    public IDisposable Observe(string jobId, Action<BulkActionJobSnapshot> onSnapshot)
    {
        void Handler(object? sender, BulkActionJobSnapshot snapshot)
        {
            if (snapshot.JobId == jobId) onSnapshot(snapshot);
        }
        JobUpdated += Handler;
        if (_jobs.TryGetValue(jobId, out var entry))
        {
            onSnapshot(entry.Snapshot);
        }
        return new UnsubscribeHandle(() => JobUpdated -= Handler);
    }

    public Task<IReadOnlyList<BulkActionJobSnapshot>> GetRecentJobsAsync(CancellationToken cancellationToken)
    {
        lock (_historyLock)
        {
            return Task.FromResult<IReadOnlyList<BulkActionJobSnapshot>>(_history.ToArray());
        }
    }

    public void ResetLocalState()
    {
        foreach (var entry in _jobs.Values)
        {
            try
            {
                entry.Cts.Cancel();
            }
            catch
            {
                // Best-effort runtime cleanup.
            }
        }

        _jobs.Clear();
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    private async Task RunAsync(JobEntry entry)
    {
        var request = entry.Request;
        var started = DateTimeOffset.UtcNow;
        UpdateSnapshot(entry, s => s with { Status = BulkActionJobStatus.Running, StartedAt = started });

        var results = new List<BulkActionResultItem>();
        var succeededIds = new HashSet<string>(StringComparer.Ordinal);
        int succeeded = 0, failed = 0, skipped = 0, canceled = 0, processed = 0;
        var progress = new Progress<BulkActionResultItem>(item =>
        {
            processed++;
            switch (item.Outcome)
            {
                case BulkActionOutcome.Succeeded: succeeded++; break;
                case BulkActionOutcome.Failed: failed++; break;
                case BulkActionOutcome.Skipped: skipped++; break;
                case BulkActionOutcome.Canceled: canceled++; break;
            }
            lock (results)
            {
                results.Add(item);
                if (item.Outcome == BulkActionOutcome.Succeeded)
                    succeededIds.Add(item.TargetId);
                var recent = results.TakeLast(25).ToArray();
                // Snapshot the full succeeded-id set so the view-model can
                // remove every deleted item from the list, not just the last 25.
                var succeededSnapshot = succeededIds.ToArray();
                UpdateSnapshot(entry, s => s with
                {
                    ProcessedCount = processed,
                    SucceededCount = succeeded,
                    FailedCount = failed,
                    SkippedCount = skipped,
                    CanceledCount = canceled,
                    RecentResults = recent,
                    SucceededTargetIds = succeededSnapshot
                });
            }
        });

        BulkActionJobStatus finalStatus;
        BulkActionResult? finalResult = null;
        try
        {
            var result = request.ActionType switch
            {
                BulkActionType.RemoveSelected => await InvokeRemoveSelectedAsync(request, progress, entry.Cts.Token),
                BulkActionType.RemoveAll => await InvokeRemoveAllAsync(request, progress, entry.Cts.Token),
                BulkActionType.DeleteCollection => await _provider.DeleteCollectionAsync(request.CollectionId ?? request.TargetIds.FirstOrDefault() ?? string.Empty, progress, entry.Cts.Token),
                BulkActionType.RemoveCollectionItems => await _provider.RemoveCollectionItemsAsync(request.CollectionId ?? string.Empty, request.TargetIds, progress, entry.Cts.Token),
                _ => throw new InvalidOperationException($"Unsupported action type {request.ActionType}")
            };
            finalResult = result;

            if (entry.Cts.IsCancellationRequested)
            {
                finalStatus = BulkActionJobStatus.Canceled;
            }
            else if (result.FailedCount == 0 && result.SkippedCount == 0)
            {
                finalStatus = BulkActionJobStatus.Succeeded;
            }
            else if (result.SucceededCount > 0)
            {
                finalStatus = BulkActionJobStatus.PartiallySucceeded;
            }
            else
            {
                finalStatus = BulkActionJobStatus.Failed;
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = BulkActionJobStatus.Canceled;
        }
        catch (Exception ex)
        {
            finalStatus = BulkActionJobStatus.Failed;
            lock (results)
            {
                results.Add(new BulkActionResultItem("<job>", BulkActionOutcome.Failed, _redaction.RedactException(ex)));
                failed++;
            }
        }

        var completed = DateTimeOffset.UtcNow;
        string[] finalSucceededIds;
        var finalProcessed = processed;
        var finalSucceeded = succeeded;
        var finalFailed = failed;
        var finalSkipped = skipped;
        var finalCanceled = canceled;
        IReadOnlyList<BulkActionResultItem> finalRecentResults;
        lock (results)
        {
            if (finalResult is not null)
            {
                finalSucceeded = finalResult.SucceededCount;
                finalFailed = finalResult.FailedCount;
                finalSkipped = finalResult.SkippedCount;
                finalCanceled = finalResult.Results.Count(r => r.Outcome == BulkActionOutcome.Canceled);
                finalProcessed = finalResult.Results.Count;
                finalSucceededIds = finalResult.Results
                    .Where(r => r.Outcome == BulkActionOutcome.Succeeded)
                    .Select(r => r.TargetId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                finalRecentResults = finalResult.Results.TakeLast(50).ToArray();
            }
            else
            {
                finalSucceededIds = succeededIds.ToArray();
                finalRecentResults = results.TakeLast(50).ToArray();
            }
        }
        UpdateSnapshot(entry, s => s with
        {
            Status = finalStatus,
            ProcessedCount = finalProcessed,
            SucceededCount = finalSucceeded,
            FailedCount = finalFailed,
            SkippedCount = finalSkipped,
            CanceledCount = finalCanceled,
            CanCancel = false,
            CompletedAt = completed,
            RecentResults = finalRecentResults,
            SucceededTargetIds = finalSucceededIds
        });

        lock (_historyLock)
        {
            _history.Insert(0, entry.Snapshot);
            if (_history.Count > 100) _history.RemoveRange(100, _history.Count - 100);
        }
    }

    private async Task<BulkActionResult> InvokeRemoveSelectedAsync(BulkActionRequest request, IProgress<BulkActionResultItem> progress, CancellationToken token)
    {
        return request.Surface switch
        {
            ActivitySurface.SavedItems => await _provider.RemoveSavedItemsAsync(request.TargetIds, progress, token),
            ActivitySurface.Likes => await _provider.RemoveLikesAsync(request.TargetIds, progress, token),
            ActivitySurface.Comments => await _provider.DeleteCommentsAsync(request.TargetIds, progress, token),
            ActivitySurface.Reposts => await _provider.RemoveRepostsAsync(request.TargetIds, progress, token),
            _ => throw new UnsupportedCapabilityException($"Remove selected is not supported for {request.Surface}.")
        };
    }

    private async Task<BulkActionResult> InvokeRemoveAllAsync(BulkActionRequest request, IProgress<BulkActionResultItem> progress, CancellationToken token)
    {
        return request.Surface switch
        {
            ActivitySurface.SavedItems => await _provider.RemoveAllSavedItemsAsync(progress, token),
            ActivitySurface.Likes => await _provider.RemoveLikesAsync(request.TargetIds, progress, token),
            ActivitySurface.Comments => await _provider.DeleteCommentsAsync(request.TargetIds, progress, token),
            ActivitySurface.Reposts => await _provider.RemoveRepostsAsync(request.TargetIds, progress, token),
            _ => throw new UnsupportedCapabilityException($"Remove all is not supported for {request.Surface}.")
        };
    }

    private void UpdateSnapshot(JobEntry entry, Func<BulkActionJobSnapshot, BulkActionJobSnapshot> update)
    {
        lock (entry.Lock)
        {
            entry.Snapshot = update(entry.Snapshot);
        }
        EmitSnapshot(entry);
    }

    private void EmitSnapshot(JobEntry entry) => JobUpdated?.Invoke(this, entry.Snapshot);

    private static string ActionLabel(BulkActionRequest r) => r.ActionType switch
    {
        BulkActionType.RemoveSelected => $"Remove selected {r.Surface}",
        BulkActionType.RemoveAll => $"Remove all {r.Surface}",
        BulkActionType.DeleteCollection => "Delete collection",
        BulkActionType.RemoveCollectionItems => "Remove items from collection",
        _ => "Bulk action"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _jobs.Values) entry.Cts.Cancel();
    }

    private sealed class JobEntry
    {
        public BulkActionRequest Request { get; }
        public CancellationTokenSource Cts { get; }
        public BulkActionJobSnapshot Snapshot { get; set; }
        public Lock Lock { get; } = new();
        public JobEntry(BulkActionRequest request, CancellationTokenSource cts, BulkActionJobSnapshot snapshot)
        {
            Request = request; Cts = cts; Snapshot = snapshot;
        }
    }

    private sealed class UnsubscribeHandle : IDisposable
    {
        private Action? _dispose;
        public UnsubscribeHandle(Action dispose) => _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
