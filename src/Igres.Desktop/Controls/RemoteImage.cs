using System.Collections.Concurrent;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Igres.Desktop.Services;

namespace Igres.Desktop.Controls;

public sealed class RemoteImage : Image
{
    public static readonly StyledProperty<string?> SourceUriProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(SourceUri));

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> BitmapCache = new(StringComparer.Ordinal);

    private CancellationTokenSource? _loadCts;

    public string? SourceUri
    {
        get => GetValue(SourceUriProperty);
        set => SetValue(SourceUriProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceUriProperty)
            StartLoad();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        CancelPendingLoad();
        base.OnDetachedFromVisualTree(e);
    }

    private void StartLoad()
    {
        CancelPendingLoad();
        Source = null;

        var uri = SourceUri;
        if (!PreviewSurfaceStyle.HasRemoteImage(uri))
            return;

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        _ = LoadAsync(uri!, cts.Token);
    }

    private async Task LoadAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await GetBitmapAsync(uri).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (bitmap is null || cancellationToken.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && string.Equals(SourceUri, uri, StringComparison.Ordinal))
                    Source = bitmap;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            BitmapCache.TryRemove(uri, out _);
        }
    }

    private static Task<Bitmap?> GetBitmapAsync(string uri) =>
        BitmapCache.GetOrAdd(uri, static key => LoadBitmapAsync(key));

    private static async Task<Bitmap?> LoadBitmapAsync(string uri)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", "IgresDesktop/1.0");
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");

            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            buffer.Position = 0;

            return new Bitmap(buffer);
        }
        catch
        {
            BitmapCache.TryRemove(uri, out _);
            return null;
        }
    }

    private static HttpClient CreateHttpClient() =>
        new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

    private void CancelPendingLoad()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }
}
