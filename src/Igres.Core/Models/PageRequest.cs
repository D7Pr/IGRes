namespace Igres.Core.Models;

public sealed record PageRequest(int PageSize, string? Cursor = null, string? SearchQuery = null, string? SortKey = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
