using Igres.Core.Models;

namespace Igres.Core.Services;

public interface IUserPreferenceService
{
    UserPreference Current { get; }
    event Action<UserPreference>? PreferenceChanged;

    Task<UserPreference> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(UserPreference preference, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
