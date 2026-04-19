using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Igres.Desktop.Services;

public sealed class AppUpdateService
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/D7Pr/IGRes/releases/latest";
    private const string ReleasePageUrl = "https://github.com/D7Pr/IGRes/releases/latest";
    public const string PortableZipAssetName = "IGRes-win-x64-portable.zip";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly string UpdatesRoot = Path.Combine(AppContext.BaseDirectory, "user-data", "updates");

    public string CurrentVersion { get; } = ResolveCurrentVersion();
    public bool CanSelfUpdate =>
        OperatingSystem.IsWindows() &&
        !string.IsNullOrWhiteSpace(Environment.ProcessPath);

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(ReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new UpdateCheckResult(false, null, $"Could not reach GitHub releases ({(int)response.StatusCode}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var release = ParseRelease(document.RootElement, PortableZipAssetName);
        if (release is null)
        {
            return new UpdateCheckResult(false, null, "No portable release asset is published yet.");
        }

        if (!IsNewerVersion(release.Version, CurrentVersion))
        {
            return new UpdateCheckResult(false, release, $"You are up to date on v{CurrentVersion}.");
        }

        return new UpdateCheckResult(true, release, $"Update v{release.Version} is available.");
    }

    public void OpenReleasePage(string? url = null)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url ?? ReleasePageUrl,
            UseShellExecute = true
        });
    }

    public async Task<PreparedUpdateResult> PrepareUpdateAsync(AppRelease release, CancellationToken cancellationToken)
    {
        if (!CanSelfUpdate)
        {
            return new PreparedUpdateResult(false, "Automatic install is only available on Windows desktop builds.", release.HtmlUrl);
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return new PreparedUpdateResult(false, "Could not determine the current executable path.", release.HtmlUrl);
        }

        Directory.CreateDirectory(UpdatesRoot);
        var versionFolder = Path.Combine(UpdatesRoot, release.Version);
        var payloadFolder = Path.Combine(versionFolder, "payload");
        if (Directory.Exists(payloadFolder))
        {
            Directory.Delete(payloadFolder, recursive: true);
        }

        Directory.CreateDirectory(versionFolder);
        var archivePath = Path.Combine(versionFolder, PortableZipAssetName);
        await DownloadFileAsync(release.AssetDownloadUrl, archivePath, cancellationToken).ConfigureAwait(false);
        ZipFile.ExtractToDirectory(archivePath, payloadFolder);

        var scriptPath = Path.Combine(versionFolder, "apply-update.cmd");
        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exeName = Path.GetFileName(processPath);
        var script = BuildUpdateScript(payloadFolder, targetDirectory, exeName);
        await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

        return new PreparedUpdateResult(true, $"Update v{release.Version} is ready. The app will restart to finish installing.", release.HtmlUrl, scriptPath);
    }

    public static AppRelease? ParseRelease(JsonElement releaseElement, string expectedAssetName)
    {
        if (!releaseElement.TryGetProperty("tag_name", out var tagNameElement))
        {
            return null;
        }

        var tagName = tagNameElement.GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        if (!releaseElement.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? assetUrl = null;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (!string.Equals(assetName, expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            assetUrl = asset.GetProperty("browser_download_url").GetString();
            break;
        }

        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            return null;
        }

        var version = NormalizeVersion(tagName);
        var htmlUrl = releaseElement.TryGetProperty("html_url", out var htmlUrlElement)
            ? htmlUrlElement.GetString() ?? ReleasePageUrl
            : ReleasePageUrl;
        var notes = releaseElement.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;
        var publishedAt = releaseElement.TryGetProperty("published_at", out var publishedAtElement)
            ? publishedAtElement.GetString()
            : null;

        return new AppRelease(version, tagName, htmlUrl, assetUrl, notes, publishedAt);
    }

    public static bool IsNewerVersion(string candidateVersion, string currentVersion)
    {
        if (Version.TryParse(NormalizeVersion(candidateVersion), out var candidate) &&
            Version.TryParse(NormalizeVersion(currentVersion), out var current))
        {
            return candidate > current;
        }

        return !string.Equals(
            NormalizeVersion(candidateVersion),
            NormalizeVersion(currentVersion),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(targetPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildUpdateScript(string sourceDirectory, string targetDirectory, string executableName)
    {
        var escapedSource = EscapeForCmd(sourceDirectory);
        var escapedTarget = EscapeForCmd(targetDirectory);
        var escapedExe = EscapeForCmd(executableName);
        return $@"@echo off
setlocal enableextensions
set ""SOURCE={escapedSource}""
set ""TARGET={escapedTarget}""
set ""EXE={escapedExe}""

for /L %%I in (1,1,90) do (
  robocopy ""%SOURCE%"" ""%TARGET%"" /E /MOVE /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >nul
  if errorlevel 8 (
    timeout /t 1 /nobreak >nul
  ) else (
    goto launch
  )
)

exit /b 1

:launch
start """" ""%TARGET%\%EXE%""
exit /b 0
";
    }

    private static string EscapeForCmd(string path) => path.Replace("^", "^^").Replace("&", "^&");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IGRes-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(AppUpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString();
        }

        return NormalizeVersion(version);
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex >= 0)
        {
            normalized = normalized[..dashIndex];
        }

        return normalized;
    }
}

public sealed record AppRelease(
    string Version,
    string TagName,
    string HtmlUrl,
    string AssetDownloadUrl,
    string Notes,
    string? PublishedAtRaw)
{
    public string PublishedAtLabel =>
        DateTimeOffset.TryParse(PublishedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishedAt)
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "Unknown publish date";
}

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    AppRelease? Release,
    string Message);

public sealed record PreparedUpdateResult(
    bool IsReadyToInstall,
    string Message,
    string? ReleaseUrl,
    string? ScriptPath = null);
