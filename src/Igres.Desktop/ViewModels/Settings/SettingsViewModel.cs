using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Igres.Core.Auth;
using Igres.Core.Models;
using Igres.Core.Providers.Real;
using Igres.Core.Services;
using Igres.Desktop.Services;
using Igres.Core.Storage;

namespace Igres.Desktop.ViewModels.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IUserPreferenceService _service;
    private readonly IAuthCoordinator _auth;
    private readonly ICapturedHeadersStore _capturedStore;
    private readonly AppUpdateService _appUpdateService;
    private readonly Action? _onCapturedChanged;
    private readonly Action? _resetLocalRuntimeState;
    private bool _suppressSave;
    private AppRelease? _availableRelease;
    private bool _startupCheckStarted;

    public event EventHandler? SignOutRequested;

    [ObservableProperty] private ThemePreference _theme = ThemePreference.System;
    [ObservableProperty] private int _pageSize = 50;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _showDiagnostics;
    [ObservableProperty] private bool _useCapturedCredentials;
    [ObservableProperty] private int _maxConcurrency = 3;
    [ObservableProperty] private bool _autoCheckForUpdates = true;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _currentVersion;
    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private string? _availableVersion;
    [ObservableProperty] private string? _updatePublishedAt;
    [ObservableProperty] private string? _updateStatus;

    [ObservableProperty] private string _pastedRequest = string.Empty;
    [ObservableProperty] private string? _capturedSummary;
    [ObservableProperty] private string? _capturedError;

    public IReadOnlyList<ThemePreference> ThemeOptions { get; } = new[]
    {
        ThemePreference.System, ThemePreference.Light, ThemePreference.Dark
    };

    public bool HasCapturedSession => !string.IsNullOrWhiteSpace(CapturedSummary);
    public bool CanInstallUpdate => IsUpdateAvailable && !IsCheckingForUpdates;
    public string UpdateBannerText => IsUpdateAvailable && !string.IsNullOrWhiteSpace(AvailableVersion)
        ? $"Update v{AvailableVersion} is available."
        : string.Empty;

    public SettingsViewModel(
        IUserPreferenceService service,
        IAuthCoordinator auth,
        ICapturedHeadersStore capturedStore,
        AppUpdateService appUpdateService,
        Action? onCapturedChanged = null,
        Action? resetLocalRuntimeState = null)
    {
        _service = service;
        _auth = auth;
        _capturedStore = capturedStore;
        _appUpdateService = appUpdateService;
        _onCapturedChanged = onCapturedChanged;
        _resetLocalRuntimeState = resetLocalRuntimeState;
        CurrentVersion = _appUpdateService.CurrentVersion;
    }

    public async Task EnsureLoadedAsync()
    {
        if (HasLoaded || IsLoading)
            return;

        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            var prefs = await _service.LoadAsync(CancellationToken.None);
            _suppressSave = true;
            Theme = prefs.Theme;
            PageSize = prefs.PageSize;
            ReducedMotion = prefs.ReducedMotion;
            ShowDiagnostics = prefs.ShowDiagnostics;
            UseCapturedCredentials = prefs.UseCapturedCredentials;
            MaxConcurrency = prefs.MaxConcurrency;
            AutoCheckForUpdates = prefs.AutoCheckForUpdates;
            _suppressSave = false;

            var creds = await _capturedStore.LoadAsync(CancellationToken.None);
            CapturedSummary = creds?.DisplaySummary;
            HasLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnThemeChanged(ThemePreference value) => ApplyThemeAndSave();
    partial void OnPageSizeChanged(int value) => PersistAsync();
    partial void OnReducedMotionChanged(bool value) => PersistAsync();
    partial void OnShowDiagnosticsChanged(bool value) => PersistAsync();
    partial void OnMaxConcurrencyChanged(int value) => PersistAsync();
    partial void OnAutoCheckForUpdatesChanged(bool value) => PersistAsync();
    partial void OnUseCapturedCredentialsChanged(bool value) => PersistAndRefreshProviderAsync();
    partial void OnCapturedSummaryChanged(string? value) => OnPropertyChanged(nameof(HasCapturedSession));
    partial void OnIsUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(CanInstallUpdate));
    partial void OnIsCheckingForUpdatesChanged(bool value) => OnPropertyChanged(nameof(CanInstallUpdate));
    partial void OnAvailableVersionChanged(string? value) => OnPropertyChanged(nameof(UpdateBannerText));

    public void InvalidateLoadedState()
    {
        HasLoaded = false;
        IsLoading = false;
    }

    private void ApplyThemeAndSave()
    {
        if (Avalonia.Application.Current is App app)
            app.ApplyTheme(Theme);

        PersistAsync();
    }

    private async void PersistAsync()
    {
        if (_suppressSave)
            return;

        var pref = BuildPreference();
        try
        {
            await _service.SaveAsync(pref, CancellationToken.None);
            Status = "Settings updated.";
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }

    private async void PersistAndRefreshProviderAsync()
    {
        if (_suppressSave)
            return;

        var pref = BuildPreference();
        try
        {
            await _service.SaveAsync(pref, CancellationToken.None);
            Status = "Session source updated.";
            _onCapturedChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SignOutAsync()
    {
        SignOutRequested?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task ClearSessionAsync()
    {
        await _auth.SignOutAsync(CancellationToken.None);

        _suppressSave = true;
        var defaults = UserPreference.Default;
        Theme = defaults.Theme;
        PageSize = defaults.PageSize;
        ReducedMotion = defaults.ReducedMotion;
        ShowDiagnostics = defaults.ShowDiagnostics;
        UseCapturedCredentials = defaults.UseCapturedCredentials;
        MaxConcurrency = defaults.MaxConcurrency;
        AutoCheckForUpdates = defaults.AutoCheckForUpdates;
        _suppressSave = false;

        await _service.ClearAsync(CancellationToken.None);

        PastedRequest = string.Empty;
        CapturedSummary = null;
        CapturedError = null;
        _resetLocalRuntimeState?.Invoke();
        _onCapturedChanged?.Invoke();
        Status = "All local app data cleared.";
        SignOutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task SaveCapturedAsync()
    {
        CapturedError = null;
        try
        {
            var parsed = CapturedRequestParser.Parse(PastedRequest);
            await _capturedStore.SaveAsync(parsed, CancellationToken.None);
            CapturedSummary = parsed.DisplaySummary;
            PastedRequest = string.Empty;
            Status = "Captured credentials saved.";
            _onCapturedChanged?.Invoke();
        }
        catch (CapturedRequestParseException ex)
        {
            CapturedError = ex.Message;
        }
        catch (Exception ex)
        {
            CapturedError = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ClearCapturedAsync()
    {
        await _capturedStore.ClearAsync(CancellationToken.None);
        CapturedSummary = null;
        PastedRequest = string.Empty;
        Status = "Captured credentials cleared.";
        _onCapturedChanged?.Invoke();
    }

    public void StartBackgroundUpdateCheck()
    {
        if (_startupCheckStarted)
            return;

        _startupCheckStarted = true;
        _ = RunBackgroundUpdateCheckAsync();
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        await EnsureLoadedAsync();
        await RunUpdateCheckCoreAsync(showUpToDateMessage: true);
    }

    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (_availableRelease is null)
        {
            UpdateStatus = "No update package is ready yet.";
            return;
        }

        IsCheckingForUpdates = true;
        try
        {
            var prepared = await _appUpdateService.PrepareUpdateAsync(_availableRelease, CancellationToken.None);
            UpdateStatus = prepared.Message;
            if (!prepared.IsReadyToInstall)
            {
                if (!string.IsNullOrWhiteSpace(prepared.ReleaseUrl))
                {
                    _appUpdateService.OpenReleasePage(prepared.ReleaseUrl);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(prepared.ScriptPath))
            {
                UpdateStatus = "Update package was prepared, but the installer could not be launched.";
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" /min \"{prepared.ScriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Could not install update: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public Task OpenUpdatePageAsync()
    {
        _appUpdateService.OpenReleasePage(_availableRelease?.HtmlUrl);
        return Task.CompletedTask;
    }

    private async Task RunBackgroundUpdateCheckAsync()
    {
        try
        {
            await EnsureLoadedAsync();
            if (!AutoCheckForUpdates)
            {
                return;
            }

            await RunUpdateCheckCoreAsync(showUpToDateMessage: false);
        }
        catch
        {
            // Background checks should stay silent unless the user asked explicitly.
        }
    }

    private async Task RunUpdateCheckCoreAsync(bool showUpToDateMessage)
    {
        if (IsCheckingForUpdates)
            return;

        IsCheckingForUpdates = true;
        try
        {
            var result = await _appUpdateService.CheckForUpdatesAsync(CancellationToken.None);
            _availableRelease = result.Release;
            IsUpdateAvailable = result.IsUpdateAvailable;
            AvailableVersion = result.Release?.Version;
            UpdatePublishedAt = result.Release?.PublishedAtLabel;
            if (showUpToDateMessage || result.IsUpdateAvailable)
            {
                UpdateStatus = result.Message;
            }
        }
        catch (Exception ex)
        {
            if (showUpToDateMessage)
            {
                UpdateStatus = $"Could not check for updates: {ex.Message}";
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private UserPreference BuildPreference() =>
        new(
            Theme,
            null,
            NormalizePageSize(PageSize),
            ReducedMotion,
            ShowDiagnostics,
            UseCapturedCredentials,
            NormalizeConcurrency(MaxConcurrency),
            AutoCheckForUpdates);

    private static int NormalizePageSize(int value) =>
        value switch
        {
            < 10 => 10,
            > 200 => 200,
            _ => value
        };

    private static int NormalizeConcurrency(int value) =>
        value switch
        {
            < 1 => 1,
            > 10 => 10,
            _ => value
        };
}
