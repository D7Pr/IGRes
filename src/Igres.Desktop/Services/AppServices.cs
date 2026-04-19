using Igres.Core.Auth;
using Igres.Core.BulkActions;
using Igres.Core.Logging;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Core.Storage;
using Igres.Desktop.ViewModels.Auth;
using Igres.Desktop.ViewModels.Comments;
using Igres.Desktop.ViewModels.Dashboard;
using Igres.Desktop.ViewModels.Jobs;
using Igres.Desktop.ViewModels.Likes;
using Igres.Desktop.ViewModels.Reposts;
using Igres.Desktop.ViewModels.SavedCollections;
using Igres.Desktop.ViewModels.SavedItems;
using Igres.Desktop.ViewModels.Settings;
using Igres.Desktop.ViewModels.Shell;
using Igres.Infrastructure.Auth;
using Igres.Infrastructure.Auth.Real;
using Igres.Infrastructure.Providers;
using Igres.Infrastructure.Providers.Mock;
using Igres.Infrastructure.Providers.Real;
using Igres.Infrastructure.Storage;

namespace Igres.Desktop.Services;

public sealed class AppServices : IDisposable
{
    public IRedactionService Redaction { get; }
    public ISecureSessionStore SessionStore { get; }
    public IUserPreferenceService UserPreferenceService { get; }
    public AppUpdateService AppUpdateService { get; }
    public IAccountActivityProvider Provider { get; }
    public IAuthCoordinator AuthCoordinator { get; }
    public IBulkActionJobRunner JobRunner { get; }
    public CollectionCleanupService CollectionCleanup { get; }
    public IConfirmationService Confirmation { get; }
    public ShellViewModel ShellViewModel { get; }

    private AppServices(
        IRedactionService redaction,
        ISecureSessionStore store,
        IUserPreferenceService prefs,
        AppUpdateService appUpdateService,
        IAccountActivityProvider provider,
        IAuthCoordinator auth,
        IBulkActionJobRunner runner,
        CollectionCleanupService collections,
        IConfirmationService confirmation,
        ShellViewModel shell)
    {
        Redaction = redaction;
        SessionStore = store;
        UserPreferenceService = prefs;
        AppUpdateService = appUpdateService;
        Provider = provider;
        AuthCoordinator = auth;
        JobRunner = runner;
        CollectionCleanup = collections;
        Confirmation = confirmation;
        ShellViewModel = shell;
    }

    public static AppServices Create()
    {
        var redaction = new RedactionService();
        var store = new SecureSessionStore();
        var prefs = new UserPreferenceService();
        var appUpdateService = new AppUpdateService();
        var capturedStore = new CapturedHeadersStore();
        var mockProvider = new MockAccountActivityProvider(() => prefs.Current.MaxConcurrency, transientFailureEvery: 23);
        var realProvider = new RealAccountActivityProvider(capturedStore, () => prefs.Current.MaxConcurrency);
        var provider = new SwitchingAccountActivityProvider(mockProvider, realProvider);
        ShellViewModel? shell = null;

        void OnCapturedChanged()
        {
            realProvider.InvalidateClient();
            try
            {
                // Run on the threadpool so any awaits inside LoadAsync don't try to resume
                // on the UI thread we may be blocking. Avoids sync-over-async deadlock.
                var latest = Task.Run(() => prefs.LoadAsync(CancellationToken.None)).GetAwaiter().GetResult();
                provider.UseReal(latest.UseCapturedCredentials);
            }
            catch
            {
                provider.UseReal(false);
            }

            shell?.HandleProviderModeChanged();
        }

        var auth = new RealAuthCoordinator(store, capturedStore, prefs, OnCapturedChanged);
        var runner = new BulkActionJobRunner(provider, redaction);
        var collections = new CollectionCleanupService(runner);
        var confirmation = new ConfirmationDialogService();

        // Apply the persisted preference at startup so the initial fetch hits the right provider.
        try
        {
            var current = Task.Run(() => prefs.LoadAsync(CancellationToken.None)).GetAwaiter().GetResult();
            provider.UseReal(current.UseCapturedCredentials);
        }
        catch { /* first run: defaults are fine */ }

        var welcome = new WelcomeViewModel(auth);
        var verification = new VerificationCodeViewModel(auth);
        var dashboard = new DashboardViewModel(provider);
        var savedItems = new SavedItemsViewModel(provider, runner, confirmation, prefs);
        var savedCollections = new SavedCollectionsViewModel(provider, collections, confirmation, prefs);
        var likes = new LikesViewModel(provider, runner, confirmation, prefs);
        var comments = new CommentsViewModel(provider, runner, confirmation, prefs);
        var reposts = new RepostsViewModel(provider, runner, confirmation, prefs);
        var jobs = new JobsViewModel(runner);

        void ResetLocalRuntimeState()
        {
            provider.UseReal(false);
            realProvider.InvalidateClient();
            mockProvider.Reset();
            runner.ResetLocalState();
            dashboard.ResetState();
            savedItems.ResetState();
            savedCollections.ResetState();
            likes.ResetState();
            comments.ResetState();
            reposts.ResetState();
            jobs.ResetState();
        }

        var settings = new SettingsViewModel(prefs, auth, capturedStore, appUpdateService, OnCapturedChanged, ResetLocalRuntimeState);

        shell = new ShellViewModel(
            auth,
            provider,
            welcome,
            verification,
            dashboard,
            savedItems,
            savedCollections,
            likes,
            comments,
            reposts,
            jobs,
            settings,
            prefs,
            capturedStore);

        return new AppServices(redaction, store, prefs, appUpdateService, provider, auth, runner, collections, confirmation, shell);
    }

    public void Dispose()
    {
        ShellViewModel.Dispose();
        if (JobRunner is IDisposable runner)
            runner.Dispose();
        if (AuthCoordinator is IDisposable auth)
            auth.Dispose();
        if (Provider is IDisposable provider)
            provider.Dispose();
        if (Confirmation is IDisposable confirmation)
            confirmation.Dispose();
    }
}
