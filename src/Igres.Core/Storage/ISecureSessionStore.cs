using Igres.Core.Models;

namespace Igres.Core.Storage;

public interface ISecureSessionStore
{
    Task SaveSessionAsync(AccountSession session, CancellationToken cancellationToken);
    Task<AccountSession?> LoadSessionAsync(CancellationToken cancellationToken);
    Task ClearSessionAsync(CancellationToken cancellationToken);
}
