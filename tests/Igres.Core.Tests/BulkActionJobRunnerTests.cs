using Igres.Core.BulkActions;
using Igres.Core.Models;
using Igres.Infrastructure.Providers.Mock;

namespace Igres.Core.Tests;

public class BulkActionJobRunnerTests
{
    [Fact]
    public async Task Queued_job_runs_to_terminal_status_with_aggregated_counts()
    {
        var provider = new MockAccountActivityProvider();
        var page = await provider.GetSavedItemsAsync(new PageRequest(50), CancellationToken.None);
        var ids = page.Items.Select(i => i.Id).Take(25).ToList();

        var runner = new BulkActionJobRunner(provider, new Igres.Core.Logging.RedactionService());
        var completion = new TaskCompletionSource<BulkActionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.JobUpdated += (_, snap) =>
        {
            if (snap.Status is BulkActionJobStatus.Succeeded
                    or BulkActionJobStatus.PartiallySucceeded
                    or BulkActionJobStatus.Failed
                    or BulkActionJobStatus.Canceled)
                completion.TrySetResult(snap);
        };

        var req = new BulkActionRequest(
            JobId: Guid.NewGuid().ToString("n"),
            ActionType: BulkActionType.RemoveSelected,
            Surface: ActivitySurface.SavedItems,
            TargetIds: ids,
            RequestedAt: DateTimeOffset.UtcNow,
            RequiresStrongConfirmation: false,
            ActionLabel: "Remove selected");

        await runner.QueueAsync(req, CancellationToken.None);

        var finalSnap = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (finalSnap.SucceededCount + finalSnap.FailedCount + finalSnap.SkippedCount + finalSnap.CanceledCount)
            .Should().Be(25);
        finalSnap.TotalCount.Should().Be(25);
    }

    [Fact]
    public async Task Cancel_moves_job_to_canceled()
    {
        var provider = new MockAccountActivityProvider();
        var page = await provider.GetSavedItemsAsync(new PageRequest(60), CancellationToken.None);
        var ids = page.Items.Select(i => i.Id).ToList();

        var runner = new BulkActionJobRunner(provider, new Igres.Core.Logging.RedactionService());
        var terminal = new TaskCompletionSource<BulkActionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.JobUpdated += (_, snap) =>
        {
            if (snap.Status is BulkActionJobStatus.Succeeded
                    or BulkActionJobStatus.PartiallySucceeded
                    or BulkActionJobStatus.Failed
                    or BulkActionJobStatus.Canceled)
                terminal.TrySetResult(snap);
        };

        var req = new BulkActionRequest(
            JobId: Guid.NewGuid().ToString("n"),
            ActionType: BulkActionType.RemoveSelected,
            Surface: ActivitySurface.SavedItems,
            TargetIds: ids,
            RequestedAt: DateTimeOffset.UtcNow,
            RequiresStrongConfirmation: false,
            ActionLabel: "Remove selected");

        var jobId = await runner.QueueAsync(req, CancellationToken.None);
        await Task.Delay(50);
        await runner.CancelAsync(jobId, CancellationToken.None);

        var finalSnap = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        finalSnap.Status.Should().BeOneOf(BulkActionJobStatus.Canceled, BulkActionJobStatus.PartiallySucceeded);
    }
}
