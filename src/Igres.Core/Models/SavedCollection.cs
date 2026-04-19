namespace Igres.Core.Models;

public sealed record SavedCollection(
    string CollectionId,
    string Name,
    IReadOnlyList<string> PreviewUris,
    int ItemCount,
    DateTimeOffset? LastUpdatedAt,
    bool SupportsDelete,
    bool SupportsItemRemoval);
