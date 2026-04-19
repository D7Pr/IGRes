using System.Text.Json;
using Igres.Desktop.Services;

namespace Igres.Desktop.Tests;

public class AppUpdateServiceTests
{
    [Fact]
    public void ParseRelease_selects_expected_portable_asset()
    {
        const string payload = """
        {
          "tag_name": "v1.2.3",
          "html_url": "https://github.com/D7Pr/IGRes/releases/tag/v1.2.3",
          "body": "Release notes",
          "published_at": "2026-04-20T00:00:00Z",
          "assets": [
            {
              "name": "IGRes-win-x64-portable.zip",
              "browser_download_url": "https://example.test/IGRes-win-x64-portable.zip"
            }
          ]
        }
        """;

        using var document = JsonDocument.Parse(payload);
        var release = AppUpdateService.ParseRelease(document.RootElement, AppUpdateService.PortableZipAssetName);

        release.Should().NotBeNull();
        release!.Version.Should().Be("1.2.3");
        release.AssetDownloadUrl.Should().Be("https://example.test/IGRes-win-x64-portable.zip");
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    public void IsNewerVersion_compares_normalized_versions(string candidate, string current, bool expected)
    {
        AppUpdateService.IsNewerVersion(candidate, current).Should().Be(expected);
    }
}
