using Igres.Core.Models;

namespace Igres.Core.Storage;

public interface ICapturedHeadersStore
{
    Task SaveAsync(CapturedHeaders headers, CancellationToken cancellationToken);
    Task<CapturedHeaders?> LoadAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
