using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Igres.Core.Models;
using Igres.Core.Services;
using Igres.Desktop.Services;
using Igres.Desktop.ViewModels.Shell;
using Igres.Desktop.Views.Shell;

namespace Igres.Desktop;

public partial class App : Application
{
    private static AppServices? _services;
    public static AppServices Services => _services ??= AppServices.Create();
    private static int _servicesDisposed;
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

    public override void Initialize()
    {
        Log("App.Initialize start");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"UnhandledException: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log($"UnobservedTaskException: {e.Exception}"); e.SetObserved(); };
        AvaloniaXamlLoader.Load(this);
        Log("App.Initialize done");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log("OnFrameworkInitializationCompleted start");
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _ = ApplyPreferenceAsync();
                Log("Resolving ShellViewModel");
                var shellVm = Services.ShellViewModel;
                Log("ShellViewModel resolved");
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var mainWindow = new ShellView
                {
                    DataContext = shellVm
                };
                Log("ShellView constructed");
                desktop.Exit += (_, _) => { Log("desktop.Exit fired"); DisposeServices(); };
                desktop.MainWindow = mainWindow;
                Log("MainWindow assigned");
                _ = shellVm.InitializeAsync();
            }
            else
            {
                Log($"Unexpected lifetime: {ApplicationLifetime?.GetType().FullName ?? "null"}");
            }
            base.OnFrameworkInitializationCompleted();
            Log("OnFrameworkInitializationCompleted done");
        }
        catch (Exception ex)
        {
            Log($"OnFrameworkInitializationCompleted FAILED: {ex}");
            throw;
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [pid {Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private async Task ApplyPreferenceAsync()
    {
        var prefs = await Services.UserPreferenceService.LoadAsync(CancellationToken.None);
        ApplyTheme(prefs.Theme);
    }

    public void ApplyTheme(ThemePreference preference)
    {
        RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private static void DisposeServices()
    {
        if (Interlocked.Exchange(ref _servicesDisposed, 1) == 1)
            return;

        Log("DisposeServices: disposing");
        try
        {
            _services?.Dispose();
        }
        catch (Exception ex)
        {
            Log($"DisposeServices: dispose threw: {ex.Message}");
        }
        finally
        {
            _services = null;
        }

        Log("DisposeServices: calling Environment.Exit");
        Environment.Exit(0);
    }
}
