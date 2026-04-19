using Igres.Core.Models;

namespace Igres.Core.BulkActions;

public interface IBulkActionJobRunner
{
    Task<string> QueueAsync(BulkActionRequest request, CancellationToken cancellationToken);
    Task CancelAsync(string jobId, CancellationToken cancellationToken);
    IDisposable Observe(string jobId, Action<BulkActionJobSnapshot> onSnapshot);
    Task<IReadOnlyList<BulkActionJobSnapshot>> GetRecentJobsAsync(CancellationToken cancellationToken);
    event EventHandler<BulkActionJobSnapshot> JobUpdated;
}
