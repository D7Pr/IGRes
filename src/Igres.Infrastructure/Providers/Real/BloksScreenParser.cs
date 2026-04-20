using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Igres.Core.Models;

namespace Igres.Infrastructure.Providers.Real;

public static class BloksScreenParser
{
    // NOTE: each captured value uses (?:\\[^"]|[^"\\])* — an atomic group with
    // unambiguous alternation (the second branch excludes '\') to prevent
    // catastrophic backtracking on long URLs that contain many \/ pairs.
    private static readonly Regex LikeItemRegex = new(
        """"""\(f4i, \(dkc, \\"media_id\\", \\"media_code\\", \\"media_product_type\\", \\"media_type\\", \\"media_image_url\\", \\"location_name\\", \\"icon\\", \\"margin_right\\"\), \(dkc, \\"(?<id>(?:\\[^"]|[^"\\])*)\\", \\"(?<code>(?:\\[^"]|[^"\\])*)\\", \\"(?<product>(?:\\[^"]|[^"\\])*)\\", \(ety, (?<mediaType>\d+)\), \\"(?<image>(?:\\[^"]|[^"\\])*)\\", \\"(?<location>(?:\\[^"]|[^"\\])*)\\", \\"(?<icon>(?:\\[^"]|[^"\\])*)\\", \\"(?<margin>(?:\\[^"]|[^"\\])*)\\"\)\)"""""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex CommentItemRegex = new(
        """"""\\"comment_id\\", \\"comment_text\\", \\"comment_time\\", \\"comment_author_id\\", \\"comment_author_username\\", \\"user_image_url\\", \\"is_self_media\\", \\"user_created_this_comment\\", \\"post_author_username\\", \\"post_media_code\\", \\"post_id\\", \\"should_include_checkbox\\", \\"is_clickable\\", \\"is_selectable\\", \\"row_may_show_shared_to_fb_text\\", \\"comment_url\\"\), \(dkc, \\"(?<commentId>(?:\\[^"]|[^"\\])*)\\", \\"(?<commentText>(?:\\[^"]|[^"\\])*)\\", \\"(?<commentTime>(?:\\[^"]|[^"\\])*)\\", \\"(?<authorId>(?:\\[^"]|[^"\\])*)\\", \\"(?<authorUsername>(?:\\[^"]|[^"\\])*)\\", \\"(?<userImage>(?:\\[^"]|[^"\\])*)\\", \(dqp, (?<isSelfMedia>true|false)\), \(dqp, (?<userCreated>true|false)\), \\"(?<postAuthor>(?:\\[^"]|[^"\\])*)\\", \\"(?<postCode>(?:\\[^"]|[^"\\])*)\\", \\"(?<postId>(?:\\[^"]|[^"\\])*)\\", \(dqp, (?<includeCheckbox>true|false)\), \(dqp, (?<isClickable>true|false)\), \(dqp, (?<isSelectable>true|false)\), \(dqp, (?<sharedToFb>true|false)\), (?:\\"(?<commentUrl>(?:\\[^"]|[^"\\])*)\\"|null)"""""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex CommentPostContextRegex = new(
        """"""\\"post_author_username\\", \\"post_text\\", \\"post_time\\", \\"post_image_url\\", \\"user_image_url\\", \\"post_id\\", \\"post_comment_id\\", \\"post_media_code\\", \\"is_self_media\\", \\"should_include_checkbox\\", \\"is_clickable\\", \\"is_selectable\\"\), \(dkc, \\"(?<postAuthor>(?:\\[^"]|[^"\\])*)\\", \\"(?<postText>(?:\\[^"]|[^"\\])*)\\", \\"(?<postTime>(?:\\[^"]|[^"\\])*)\\", \\"(?<postImage>(?:\\[^"]|[^"\\])*)\\", \\"(?<userImage>(?:\\[^"]|[^"\\])*)\\", \\"(?<postId>(?:\\[^"]|[^"\\])*)\\", \\"(?<postCommentId>(?:\\[^"]|[^"\\])*)\\", \\"(?<postCode>(?:\\[^"]|[^"\\])*)\\", \(dqp, (?<isSelfMedia>true|false)\), \(dqp, (?<includeCheckbox>true|false)\), \(dqp, (?<isClickable>true|false)\), \(dqp, (?<isSelectable>true|false)\)"""""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex RepostRowRegex = new(
        """\(dnt, \\"(?<state>\d+)_30\\"\), \(f4i, \(dkc, \\"(?<id>\d{16,}_\d{5,})\\"\), \(dkc, \(dqp, true\)\)\)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex RepostIdRegex = new(
        """\(dkc, \\"(?<id>\d{16,}_\d{5,})\\"\)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UrlRegex = new(
        """https:\\/\\/(?:\\\/|[^\s"\\])+""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Tries to extract the next-page cursor that Instagram embeds in the Bloks payload.
    // Covers both the direct \"next_max_id\", \"VALUE\" pattern and the dkc tuple form.
    private static readonly Regex NextCursorRegex = new(
        @"\\""next_max_id\\"", \\""(?<cursor>(?:\\[^""]|[^""\\])+)\\"""
        + @"|\(dkc, \\""next_max_id\\"", \\""(?<cursor2>(?:\\[^""]|[^""\\])+)\\""(?:, |\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private const string RepostContainerMarker = "media_repost_container_non_empty_state";

    public static BloksSurfaceData ParseLikes(string body, DateTimeOffset referenceTime)
    {
        var payload = ExtractPayload(body);
        var items = new List<ActivityItem>();

        foreach (Match match in LikeItemRegex.Matches(payload))
        {
            var id = Decode(match.Groups["id"].Value);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var code = Decode(match.Groups["code"].Value);
            var product = Decode(match.Groups["product"].Value);
            var preview = Decode(match.Groups["image"].Value);
            var location = Decode(match.Groups["location"].Value);
            var mediaType = ParseInt(match.Groups["mediaType"].Value);
            var kind = MapMediaKind(mediaType);
            var isReel = string.Equals(product, "clips", StringComparison.OrdinalIgnoreCase);
            var text = !string.IsNullOrWhiteSpace(location)
                ? location
                : isReel ? "Liked reel" : "Liked post";
            var sourceUri = !string.IsNullOrWhiteSpace(code)
                ? $"https://www.instagram.com/{(isReel ? "reel" : "p")}/{code}/"
                : null;

            items.Add(new ActivityItem(
                Id: id,
                Surface: ActivitySurface.Likes,
                MediaKind: kind,
                PreviewUri: preview,
                AuthorHandle: null,
                AuthorDisplayName: "Instagram",
                TextSnippet: text,
                OccurredAt: null,
                SourceUri: sourceUri,
                ParentCollectionId: null,
                SupportsSelection: true,
                SupportsDelete: true,
                Metadata: new Dictionary<string, string>
                {
                    ["product_type"] = product,
                    ["media_code"] = code
                }));
        }

        return new BloksSurfaceData(items, ParseDeleteContext(
            payload,
            "com.instagram.privacy.activity_center.liked_unlike",
            "/api/v1/bloks/apps/com.instagram.privacy.activity_center.liked_unlike/"),
            ParseNextCursor(payload));
    }

    public static BloksSurfaceData ParseComments(string body, DateTimeOffset referenceTime)
    {
        var payload = ExtractPayload(body);
        var items = new List<ActivityItem>();

        foreach (Match match in CommentItemRegex.Matches(payload))
        {
            var commentId = Decode(match.Groups["commentId"].Value);
            var postId = Decode(match.Groups["postId"].Value);
            if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(postId))
                continue;

            var text = Decode(match.Groups["commentText"].Value);
            var authorUsername = Decode(match.Groups["authorUsername"].Value);
            var postAuthor = Decode(match.Groups["postAuthor"].Value);
            var userImage = Decode(match.Groups["userImage"].Value);
            var postCode = Decode(match.Groups["postCode"].Value);
            var commentUrl = Decode(match.Groups["commentUrl"].Value);
            var postContext = FindCommentPostContext(payload, match.Index, postId);
            var compositeId = $"{postId}:{commentId}";
            var sourceUri = !string.IsNullOrWhiteSpace(commentUrl)
                ? commentUrl
                : !string.IsNullOrWhiteSpace(postCode) ? $"https://www.instagram.com/p/{postCode}/" : null;
            var previewUri = !string.IsNullOrWhiteSpace(postContext.PostImageUrl) ? postContext.PostImageUrl : userImage;
            var kind = sourceUri?.Contains("/reel/", StringComparison.OrdinalIgnoreCase) == true
                ? MediaKind.Video
                : MediaKind.Image;

            items.Add(new ActivityItem(
                Id: compositeId,
                Surface: ActivitySurface.Comments,
                MediaKind: kind,
                PreviewUri: previewUri,
                AuthorHandle: authorUsername,
                AuthorDisplayName: null,
                TextSnippet: text,
                OccurredAt: ParseRelativeTime(Decode(match.Groups["commentTime"].Value), referenceTime),
                SourceUri: sourceUri,
                ParentCollectionId: null,
                SupportsSelection: true,
                SupportsDelete: true,
                Metadata: new Dictionary<string, string>
                {
                    ["comment_id"] = commentId,
                    ["post_id"] = postId,
                    ["post_author_username"] = postAuthor,
                    ["post_media_code"] = postCode,
                    ["preview_source"] = !string.IsNullOrWhiteSpace(postContext.PostImageUrl) ? "post_media" : "author_avatar"
                }));
        }

        return new BloksSurfaceData(items, ParseDeleteContext(
            payload,
            "com.instagram.privacy.activity_center.comments_delete",
            "/api/v1/bloks/apps/com.instagram.privacy.activity_center.comments_delete/"),
            ParseNextCursor(payload));
    }

    public static BloksSurfaceData ParseReposts(string body, DateTimeOffset referenceTime)
    {
        var payload = ExtractPayload(body);
        var section = ExtractRepostSection(payload);
        var items = ParseStructuredReposts(section);
        if (items.Count == 0)
            items = ParseFallbackReposts(section);

        return new BloksSurfaceData(items, ParseDeleteContext(
            payload,
            "com.instagram.privacy.activity_center.media_repost_delete",
            "/api/v1/bloks/apps/com.instagram.privacy.activity_center.media_repost_delete/"),
            ParseNextCursor(payload));
    }

    private static List<ActivityItem> ParseStructuredReposts(string section)
    {
        var items = new List<ActivityItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in RepostRowRegex.Matches(section))
        {
            var id = Decode(match.Groups["id"].Value);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;

            var nextRowIndex = section.IndexOf("(dnt, \\\"", match.Index + match.Length, StringComparison.Ordinal);
            var windowEnd = nextRowIndex >= 0 ? nextRowIndex : section.Length;
            var window = section.Substring(match.Index, windowEnd - match.Index);
            var preview = ExtractPreviewFromWindow(window);
            var kind = ExtractRepostKind(window);
            var label = ExtractRepostLabel(window);
            var kindLabel = RepostKindLabel(kind);

            items.Add(new ActivityItem(
                Id: id,
                Surface: ActivitySurface.Reposts,
                MediaKind: kind,
                PreviewUri: preview,
                AuthorHandle: null,
                AuthorDisplayName: kindLabel,
                TextSnippet: label ?? kindLabel,
                OccurredAt: null,
                SourceUri: null,
                ParentCollectionId: null,
                SupportsSelection: true,
                SupportsDelete: true,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["row_state"] = match.Groups["state"].Value
                }));
        }

        return items;
    }

    private static List<ActivityItem> ParseFallbackReposts(string section)
    {
        var orderedIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in RepostIdRegex.Matches(section))
        {
            var id = match.Groups["id"].Value;
            if (GetLeadingDigitsLength(id) > 19)
                continue;

            if (seen.Add(id))
                orderedIds.Add(id);
        }

        var items = new List<ActivityItem>(orderedIds.Count);
        foreach (var id in orderedIds)
        {
            items.Add(new ActivityItem(
                Id: id,
                Surface: ActivitySurface.Reposts,
                MediaKind: MediaKind.Unknown,
                PreviewUri: null,
                AuthorHandle: null,
                AuthorDisplayName: "Reposted item",
                TextSnippet: $"Reposted item {ShortId(id)}",
                OccurredAt: null,
                SourceUri: null,
                ParentCollectionId: null,
                SupportsSelection: true,
                SupportsDelete: true));
        }

        return items;
    }

    private static string ExtractPayload(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Instagram returned a Bloks response without a payload string.");

        return NormalizePayload(payload.GetString() ?? string.Empty);
    }

    private static string NormalizePayload(string payload) =>
        payload
            .Replace(@"\(", "(", StringComparison.Ordinal)
            .Replace(@"\)", ")", StringComparison.Ordinal);

    private static BloksDeleteContext ParseDeleteContext(string payload, string actionName, string endpointPath)
    {
        var actionIndex = payload.IndexOf($@"\""{actionName}\""", StringComparison.Ordinal);
        if (actionIndex < 0)
            actionIndex = payload.IndexOf(actionName, StringComparison.Ordinal);
        if (actionIndex < 0)
            throw new InvalidOperationException($"Could not locate delete context for {actionName}.");

        var searchWindow = payload.Substring(actionIndex, Math.Min(4000, payload.Length - actionIndex));
        var match = Regex.Match(
            searchWindow,
            """\(ety, (?<container>\d+)\), \(ety, (?<element>\d+)\), \(ety, (?<spinner>\d+)\),""",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!match.Success)
            throw new InvalidOperationException($"Could not locate delete context for {actionName}.");

        return new BloksDeleteContext(
            EndpointPath: endpointPath,
            ContentContainerId: ParseInt(match.Groups["container"].Value),
            ContentElementId: ParseInt(match.Groups["element"].Value),
            ContentSpinnerId: ParseInt(match.Groups["spinner"].Value));
    }

    private static string ExtractRepostSection(string payload)
    {
        // The marker appears AFTER all repost rows in the payload, so search the
        // span up to (and slightly past) the marker rather than centering on it.
        var markerIndex = payload.IndexOf(RepostContainerMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return payload;

        var end = Math.Min(payload.Length, markerIndex + 5000);
        return payload[..end];
    }

    private static string? ExtractPreviewFromWindow(string window)
    {
        foreach (Match match in UrlRegex.Matches(window))
        {
            var candidate = Decode(match.Value);
            if (candidate.Contains("instagram", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("fbcdn", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static MediaKind ExtractRepostKind(string window)
    {
        if (window.Contains("reels__filled__32", StringComparison.Ordinal)
            || window.Contains("clips__filled__32", StringComparison.Ordinal))
        {
            return MediaKind.Video;
        }

        if (window.Contains("carousel-prism__filled__32", StringComparison.Ordinal)
            || window.Contains("carousel__filled__32", StringComparison.Ordinal))
        {
            return MediaKind.Carousel;
        }

        return MediaKind.Image;
    }

    private static string? ExtractRepostLabel(string window)
    {
        foreach (Match match in Regex.Matches(
                     window,
                     "\\\\\\\"\\)\\\\\\\":\\\\\\\"(?<label>(?:\\\\[^\"]|[^\"\\\\])*)\\\\\\\",\\\\\\\"-\\\\\\\":\\\\\\\"12sp\\\\\\\"",
                     RegexOptions.CultureInvariant))
        {
            var candidate = Decode(match.Groups["label"].Value).Trim();
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (candidate is "visible" or "gone")
                continue;

            return candidate;
        }

        return null;
    }

    private static string RepostKindLabel(MediaKind kind) => kind switch
    {
        MediaKind.Video => "Reposted reel",
        MediaKind.Carousel => "Reposted carousel",
        MediaKind.Image => "Reposted post",
        _ => "Reposted item"
    };

    private static int GetLeadingDigitsLength(string value)
    {
        var underscore = value.IndexOf('_');
        return underscore < 0 ? value.Length : underscore;
    }

    private static string Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return string.Empty;

        // First pass: undo the JSON-style escaping used by Bloks payloads.
        // E.g. "\\u00e9" -> "\u00e9" (literal six chars) for double-encoded
        // values, or "\/" -> "/" for URL-style fields.
        string first;
        try
        {
            first = JsonSerializer.Deserialize<string>($"\"{encoded}\"") ?? encoded;
        }
        catch
        {
            return encoded;
        }

        // Bloks emoji/unicode values are doubly-encoded — after the first
        // decode we still see literal "\uXXXX" (six chars) instead of the
        // glyph. Run a second pass when escapes remain so emojis render
        // correctly. URLs that already decoded to plain text won't contain
        // a backslash so they're untouched.
        if (first.IndexOf('\\') < 0)
            return first;

        try
        {
            // Re-deserialize. Any literal " inside `first` would break the
            // outer quoting, so escape bare quotes first. We do NOT re-escape
            // backslashes because the literal \uXXXX sequences are exactly
            // what we want JSON to interpret.
            var quoted = "\"" + first.Replace("\"", "\\\"") + "\"";
            return JsonSerializer.Deserialize<string>(quoted) ?? first;
        }
        catch
        {
            return first;
        }
    }

    private static MediaKind MapMediaKind(int mediaType) => mediaType switch
    {
        2 => MediaKind.Video,
        8 => MediaKind.Carousel,
        1 => MediaKind.Image,
        _ => MediaKind.Unknown
    };

    private static DateTimeOffset? ParseRelativeTime(string value, DateTimeOffset referenceTime)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (value.Length < 2)
            return null;

        if (!int.TryParse(value[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            return null;

        return char.ToLowerInvariant(value[^1]) switch
        {
            's' => referenceTime.AddSeconds(-amount),
            'm' => referenceTime.AddMinutes(-amount),
            'h' => referenceTime.AddHours(-amount),
            'd' => referenceTime.AddDays(-amount),
            'w' => referenceTime.AddDays(-7 * amount),
            _ => null
        };
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static string? ParseNextCursor(string payload)
    {
        var m = NextCursorRegex.Match(payload);
        if (!m.Success) return null;
        var raw = m.Groups["cursor"].Success ? m.Groups["cursor"].Value : m.Groups["cursor2"].Value;
        var decoded = Decode(raw);
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string ShortId(string id) =>
        id.Length <= 8 ? id : id[..8];

    private static CommentPostContext FindCommentPostContext(string payload, int anchorIndex, string postId)
    {
        var start = Math.Max(0, anchorIndex - 6000);
        var window = payload[start..anchorIndex];
        CommentPostContext? latestMatch = null;

        foreach (Match match in CommentPostContextRegex.Matches(window))
        {
            var candidatePostId = Decode(match.Groups["postId"].Value);
            if (!string.Equals(candidatePostId, postId, StringComparison.Ordinal))
                continue;

            latestMatch = new CommentPostContext(
                Decode(match.Groups["postImage"].Value),
                Decode(match.Groups["postCode"].Value),
                Decode(match.Groups["postAuthor"].Value));
        }

        return latestMatch ?? new CommentPostContext(null, null, null);
    }
}

public sealed record BloksSurfaceData(IReadOnlyList<ActivityItem> Items, BloksDeleteContext DeleteContext, string? NextCursor = null);

public sealed record BloksDeleteContext(
    string EndpointPath,
    int ContentContainerId,
    int ContentElementId,
    int ContentSpinnerId);

internal sealed record CommentPostContext(string? PostImageUrl, string? PostCode, string? PostAuthorUsername);
