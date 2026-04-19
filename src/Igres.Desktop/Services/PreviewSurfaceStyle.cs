using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Media;

namespace Igres.Desktop.Services;

public static class PreviewSurfaceStyle
{
    private static readonly ConcurrentDictionary<string, IBrush> BrushCache = new(StringComparer.Ordinal);
    private static readonly char[] TokenSeparators = [' ', '.', '_', '-', '/', '\\'];

    public static bool HasRemoteImage(string? previewUri)
    {
        if (!Uri.TryCreate(previewUri, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    public static IBrush GetBrush(string? previewUri, string? seed = null)
    {
        var cacheKey = !string.IsNullOrWhiteSpace(previewUri)
            ? previewUri
            : $"seed::{seed ?? "preview"}";

        return BrushCache.GetOrAdd(cacheKey, _ => CreateBrush(previewUri, seed));
    }

    public static string GetMonogram(string? primary, string? secondary = null)
    {
        var tokens = Tokenize(primary)
            .Concat(Tokenize(secondary))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Take(2)
            .Select(token => char.ToUpperInvariant(token[0]))
            .ToArray();

        return tokens.Length switch
        {
            0 => "IG",
            1 => tokens[0].ToString(CultureInfo.InvariantCulture),
            _ => new string(tokens)
        };
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IBrush CreateBrush(string? previewUri, string? seed)
    {
        var hue = TryReadPreviewHue(previewUri)
            ?? ComputeHue(previewUri ?? seed ?? "igres");

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative)
        };

        var (r1, g1, b1) = HslToRgb(hue, 0.68, 0.58);
        var (r2, g2, b2) = HslToRgb((hue + 32) % 360, 0.72, 0.43);
        var (r3, g3, b3) = HslToRgb((hue + 72) % 360, 0.78, 0.28);

        gradient.GradientStops.Add(new GradientStop(Color.FromRgb((byte)r1, (byte)g1, (byte)b1), 0.0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb((byte)r2, (byte)g2, (byte)b2), 0.55));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb((byte)r3, (byte)g3, (byte)b3), 1.0));

        return gradient;
    }

    private static int? TryReadPreviewHue(string? previewUri)
    {
        if (string.IsNullOrWhiteSpace(previewUri))
            return null;

        var marker = previewUri.IndexOf("hue/", StringComparison.Ordinal);
        if (marker < 0)
            return null;

        var tail = previewUri[(marker + 4)..];
        var slash = tail.IndexOf('/');
        if (slash <= 0)
            return null;

        return int.TryParse(tail[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed % 360
            : null;
    }

    private static int ComputeHue(string seed)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in seed)
                hash = (hash * 31) + ch;

            return Math.Abs(hash) % 360;
        }
    }

    private static (int r, int g, int b) HslToRgb(double h, double s, double l)
    {
        var c = (1 - Math.Abs((2 * l) - 1)) * s;
        var hh = h / 60.0;
        var x = c * (1 - Math.Abs((hh % 2) - 1));
        var r1 = 0.0;
        var g1 = 0.0;
        var b1 = 0.0;

        if (hh < 1) { r1 = c; g1 = x; }
        else if (hh < 2) { r1 = x; g1 = c; }
        else if (hh < 3) { g1 = c; b1 = x; }
        else if (hh < 4) { g1 = x; b1 = c; }
        else if (hh < 5) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        var m = l - (c / 2);
        return ((int)((r1 + m) * 255), (int)((g1 + m) * 255), (int)((b1 + m) * 255));
    }
}
