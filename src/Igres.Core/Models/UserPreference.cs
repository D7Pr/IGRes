namespace Igres.Core.Models;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public sealed record UserPreference(
    ThemePreference Theme,
    ActivitySurface? LastSurface,
    int PageSize,
    bool ReducedMotion,
    bool ShowDiagnostics,
    bool UseCapturedCredentials = false,
    // How many HTTP batches may run in parallel during bulk actions (1-10).
    int MaxConcurrency = 3,
    bool AutoCheckForUpdates = true)
{
    public static UserPreference Default => new(ThemePreference.System, null, 50, false, false, false, 3, true);
}
