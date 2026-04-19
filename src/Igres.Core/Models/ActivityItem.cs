namespace Igres.Core.Models;

public enum MediaKind
{
    Image,
    Video,
    Carousel,
    Comment,
    Unknown
}

public sealed record ActivityItem(
    string Id,
    ActivitySurface Surface,
    MediaKind MediaKind,
    string? PreviewUri,
    string? AuthorHandle,
    string? AuthorDisplayName,
    string? TextSnippet,
    DateTimeOffset? OccurredAt,
    string? SourceUri,
    string? ParentCollectionId,
    bool SupportsSelection,
    bool SupportsDelete,
    IReadOnlyDictionary<string, string>? Metadata = null);
