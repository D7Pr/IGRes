namespace Igres.Core.Models;

public sealed record ProviderCapability(
    ActivitySurface Surface,
    bool CanList,
    bool CanDeleteSelected,
    bool CanDeleteAll,
    bool CanDeleteCollection,
    bool RequiresAuth,
    string? Notes = null);
