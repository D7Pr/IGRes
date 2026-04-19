using Avalonia.Media;
using Igres.Desktop.Services;

namespace Igres.Desktop.Tests;

public sealed class PreviewSurfaceStyleTests
{
    [Fact]
    public void HasRemoteImage_only_accepts_http_and_https()
    {
        PreviewSurfaceStyle.HasRemoteImage("https://cdn.example.com/image.jpg").Should().BeTrue();
        PreviewSurfaceStyle.HasRemoteImage("http://cdn.example.com/image.jpg").Should().BeTrue();
        PreviewSurfaceStyle.HasRemoteImage("preview://hue/120/1").Should().BeFalse();
        PreviewSurfaceStyle.HasRemoteImage("not-a-uri").Should().BeFalse();
    }

    [Fact]
    public void GetMonogram_uses_first_letters_from_available_text()
    {
        PreviewSurfaceStyle.GetMonogram("quiet studio", "nora alex").Should().Be("QS");
        PreviewSurfaceStyle.GetMonogram(null, "field notes").Should().Be("FN");
    }

    [Fact]
    public void GetBrush_returns_stable_cached_brushes()
    {
        var first = PreviewSurfaceStyle.GetBrush("preview://hue/210/123", "sample");
        var second = PreviewSurfaceStyle.GetBrush("preview://hue/210/123", "different");

        first.Should().BeSameAs(second);
        first.Should().BeAssignableTo<IBrush>();
    }
}
