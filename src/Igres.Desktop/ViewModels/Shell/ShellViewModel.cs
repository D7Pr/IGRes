using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.Auth;
using Igres.Core.Models;
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

namespace Igres.Desktop.ViewModels.Shell;

public sealed partial class ShellViewModel : ViewModelBase, IDisposable
{
    public IAuthCoordinator Auth { get; }
    public IAccountActivityProvider Provider { get; }

    public WelcomeViewModel Welcome { get; }
    public VerificationCodeViewModel Verification { get; }
    public DashboardViewModel Dashboard { get; }
    public SavedItemsViewModel SavedItems { get; }
    public SavedCollectionsViewModel SavedCollections { get; }
    public LikesViewModel Likes { get; }
    public CommentsViewModel Comments { get; }
    public RepostsViewModel Reposts { get; }
    public JobsViewModel Jobs { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private AccountSession? _session;
    [ObservableProperty] private ViewModelBase? _currentContent;
    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private string _providerLabel = "Local session";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public bool IsSignedIn => Session?.State == AccountSessionState.SignedIn;
    public bool IsAwaitingVerification => Session?.State == AccountSessionState.AwaitingVerification;
    public bool IsAuthenticated => IsSignedIn;

    public bool IsDashboardActive => ReferenceEquals(CurrentContent, Dashboard);
    public bool IsSavedItemsActive => ReferenceEquals(CurrentContent, SavedItems);
    public bool IsSavedCollectionsActive => ReferenceEquals(CurrentContent, SavedCollections);
    public bool IsLikesActive => ReferenceEquals(CurrentContent, Likes);
    public bool IsCommentsActive => ReferenceEquals(CurrentContent, Comments);
    public bool IsRepostsActive => ReferenceEquals(CurrentContent, Reposts);
    public bool IsJobsActive => ReferenceEquals(CurrentContent, Jobs);
    public bool IsSettingsActive => ReferenceEquals(CurrentContent, Settings);

    private readonly IUserPreferenceService? _prefs;
    private readonly ICapturedHeadersStore? _capturedStore;

    public ShellViewModel(
        IAuthCoordinator auth,
        IAccountActivityProvider provider,
        WelcomeViewModel welcome,
        VerificationCodeViewModel verification,
        DashboardViewModel dashboard,
        SavedItemsViewModel savedItems,
        SavedCollectionsViewModel savedCollections,
        LikesViewModel likes,
        CommentsViewModel comments,
        RepostsViewModel reposts,
        JobsViewModel jobs,
        SettingsViewModel settings,
        IUserPreferenceService? prefs = null,
        ICapturedHeadersStore? capturedStore = null)
    {
        Auth = auth;
        Provider = provider;
        _prefs = prefs;
        _capturedStore = capturedStore;
        Welcome = welcome;
        Verification = verification;
        Dashboard = dashboard;
        SavedItems = savedItems;
        SavedCollections = savedCollections;
        Likes = likes;
        Comments = comments;
        Reposts = reposts;
        Jobs = jobs;
        Settings = settings;

        UpdateProviderLabel();

        Welcome.ChallengeIssued += OnChallengeIssued;
        Welcome.SignedIn += OnSignedIn;
        Verification.SignedIn += OnSignedIn;
        Verification.Restarted += OnRestartAuth;
        Settings.SignOutRequested += async (_, _) => await SignOutAsync();

        BuildNav();
    }

    private void BuildNav()
    {
        NavItems.Clear();
        NavItems.Add(new NavItem("Overview", ActivitySurface.SavedItems, Dashboard, isTopLevel: true));
        NavItems.Add(new NavItem("Saved items", ActivitySurface.SavedItems, SavedItems));
        NavItems.Add(new NavItem("Saved collections", ActivitySurface.SavedCollections, SavedCollections));
        NavItems.Add(new NavItem("Reposts", ActivitySurface.Reposts, Reposts));
        NavItems.Add(new NavItem("Likes", ActivitySurface.Likes, Likes));
        NavItems.Add(new NavItem("Comments", ActivitySurface.Comments, Comments));
        NavItems.Add(new NavItem("Jobs", ActivitySurface.SavedItems, Jobs, isTopLevel: true));
        NavItems.Add(new NavItem("Settings", ActivitySurface.SavedItems, Settings, isTopLevel: true));
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;

            if (_prefs is not null && _capturedStore is not null)
            {
                var prefs = await _prefs.LoadAsync(CancellationToken.None);
                if (prefs.UseCapturedCredentials)
                {
                    var captured = await _capturedStore.LoadAsync(CancellationToken.None);
                    if (captured is not null)
                    {
                        Session = new AccountSession(
                            AccountId: $"captured-{captured.IgUDsUserId}",
                            DisplayName: $"Captured session ({captured.DisplaySummary})",
                            Handle: captured.IgUDsUserId,
                            State: AccountSessionState.SignedIn,
                            LastAuthenticatedAt: captured.CapturedAt,
                            HasPersistentCredentials: true,
                            ProviderName: Provider.ProviderName);
                        UpdateProviderLabel();
                        NotifySessionState();
                        Settings.StartBackgroundUpdateCheck();
                        await ShowDashboardAsync();
                        return;
                    }
                }
            }

            var restored = await Auth.RestoreSessionAsync(CancellationToken.None);
            if (restored?.State == AccountSessionState.SignedIn)
            {
                Session = restored;
                UpdateProviderLabel();
                NotifySessionState();
                Settings.StartBackgroundUpdateCheck();
                await ShowDashboardAsync();
            }
            else
            {
                Session = AccountSession.SignedOut(Provider.ProviderName);
                UpdateProviderLabel();
                CurrentContent = Welcome;
            }
        }
        finally
        {
            IsBusy = false;
            Settings.StartBackgroundUpdateCheck();
        }
    }

    private async void OnChallengeIssued(object? sender, VerificationChallenge challenge)
    {
        await RunOnUiThreadAsync(() =>
        {
            ResetLoadedContent();
            Session = new AccountSession(string.Empty, string.Empty, Welcome.Identifier, AccountSessionState.AwaitingVerification, null, false, Provider.ProviderName);
            Verification.LoadChallenge(challenge);
            CurrentContent = Verification;
            UpdateProviderLabel();
            NotifySessionState();
        });

        await Task.CompletedTask;
    }

    private async void OnSignedIn(object? sender, AccountSession session)
    {
        await RunOnUiThreadAsync(() =>
        {
            ResetLoadedContent();
            Session = session;
            UpdateProviderLabel();
            NotifySessionState();
        });

        await ShowDashboardAsync();
    }

    private async void OnRestartAuth(object? sender, EventArgs e)
    {
        await RunOnUiThreadAsync(() =>
        {
            ResetLoadedContent();
            Session = AccountSession.SignedOut(Provider.ProviderName);
            SelectedNav = null;
            UpdateProviderLabel();
            NotifySessionState();
            Welcome.Reset();
            CurrentContent = Welcome;
        });

        await Task.CompletedTask;
    }

    private async Task ShowDashboardAsync()
    {
        await RunOnUiThreadAsync(async () =>
        {
            SelectedNav = NavItems.FirstOrDefault();
            CurrentContent = Dashboard;
            await Dashboard.EnsureLoadedAsync();
        });
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        foreach (var navItem in NavItems)
            navItem.IsSelected = ReferenceEquals(navItem, value);

        if (value is null)
            return;

        CurrentContent = value.Content;
        _ = LoadContentAsync(value.Content);
    }

    partial void OnCurrentContentChanged(ViewModelBase? value) => RaiseActiveContentChanged();

    [RelayCommand]
    public async Task SignOutAsync()
    {
        await Auth.SignOutAsync(CancellationToken.None);
        ResetLoadedContent();
        Session = AccountSession.SignedOut(Provider.ProviderName);
        SelectedNav = null;
        Welcome.Reset();
        CurrentContent = Welcome;
        UpdateProviderLabel();
        NotifySessionState();
    }

    [RelayCommand]
    private void SelectNav(NavItem? item)
    {
        if (item is null)
            return;

        SelectedNav = item;
    }

    public void HandleProviderModeChanged()
    {
        _ = RunOnUiThreadAsync(async () =>
        {
            UpdateProviderLabel();
            ResetLoadedContent();

            if (!IsSignedIn)
                return;

            await LoadContentAsync(SelectedNav?.Content ?? Dashboard);
        });
    }

    private Task LoadContentAsync(ViewModelBase content) =>
        content switch
        {
            SavedItemsViewModel v => v.EnsureLoadedAsync(),
            SavedCollectionsViewModel c => c.EnsureLoadedAsync(),
            LikesViewModel l => l.EnsureLoadedAsync(),
            CommentsViewModel cm => cm.EnsureLoadedAsync(),
            RepostsViewModel r => r.EnsureLoadedAsync(),
            JobsViewModel j => j.EnsureLoadedAsync(),
            SettingsViewModel s => s.EnsureLoadedAsync(),
            DashboardViewModel d => d.EnsureLoadedAsync(),
            _ => Task.CompletedTask
        };

    private void UpdateProviderLabel()
    {
        var name = Provider.ProviderName;
        ProviderLabel = name.Contains("Instagram", StringComparison.OrdinalIgnoreCase)
            ? "Instagram session"
            : name.Contains("Mock", StringComparison.OrdinalIgnoreCase)
                ? "Local session"
                : "Protected session";
    }

    private void ResetLoadedContent()
    {
        Dashboard.ResetState();
        SavedItems.ResetState();
        SavedCollections.ResetState();
        Likes.ResetState();
        Comments.ResetState();
        Reposts.ResetState();
        Jobs.ResetState();
    }

    private void RaiseActiveContentChanged()
    {
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsSavedItemsActive));
        OnPropertyChanged(nameof(IsSavedCollectionsActive));
        OnPropertyChanged(nameof(IsLikesActive));
        OnPropertyChanged(nameof(IsCommentsActive));
        OnPropertyChanged(nameof(IsRepostsActive));
        OnPropertyChanged(nameof(IsJobsActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

    private void NotifySessionState()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsAwaitingVerification));
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(ProviderLabel));
    }

    public void Dispose()
    {
        Welcome.ChallengeIssued -= OnChallengeIssued;
        Welcome.SignedIn -= OnSignedIn;
        Verification.SignedIn -= OnSignedIn;
        Verification.Restarted -= OnRestartAuth;
    }
}

public sealed partial class NavItem : ObservableObject
{
    public string Label { get; }
    public ActivitySurface Surface { get; }
    public ViewModelBase Content { get; }
    public bool IsTopLevel { get; }

    [ObservableProperty] private bool _isSelected;

    public NavItem(string label, ActivitySurface surface, ViewModelBase content, bool isTopLevel = false)
    {
        Label = label;
        Surface = surface;
        Content = content;
        IsTopLevel = isTopLevel;
    }
}
