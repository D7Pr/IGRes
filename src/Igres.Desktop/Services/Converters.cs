using System.Globalization;
using Avalonia.Data.Converters;
using Igres.Core.Models;

namespace Igres.Desktop.Services;

public sealed class PositiveIntConverter : IValueConverter
{
    public static readonly PositiveIntConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class IsNullConverter : IValueConverter
{
    public static readonly IsNullConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class JobStatusBrushConverter : IValueConverter
{
    public static readonly JobStatusBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            BulkActionJobStatus.Succeeded => "AccentPositiveBrush",
            BulkActionJobStatus.PartiallySucceeded => "AccentWarningBrush",
            BulkActionJobStatus.Failed => "AccentDestructiveBrush",
            BulkActionJobStatus.Canceled => "TextSubtleBrush",
            BulkActionJobStatus.Running => "AccentBrush",
            _ => "TextSubtleBrush"
        };
        var app = Avalonia.Application.Current;
        if (app is not null && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var res))
            return res;
        return null;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class PreviewHueBrushConverter : IValueConverter
{
    public static readonly PreviewHueBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PreviewSurfaceStyle.GetBrush(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
