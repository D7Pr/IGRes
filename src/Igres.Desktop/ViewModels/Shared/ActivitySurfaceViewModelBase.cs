using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.BulkActions;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.Services;

namespace Igres.Desktop.ViewModels.Shared;

public abstract partial class ActivitySurfaceViewModelBase : ViewModelBase
{
    protected IAccountActivityProvider Provider { get; }
    protected IBulkActionJobRunner Runner { get; }
    protected IConfirmationService Confirmation { get; }
    protected IUserPreferenceService Preferences { get; }

    public abstract ActivitySurface Surface { get; }
    public abstract string Title { get; }
    public abstract string RemoveSelectedActionLabel { get; }
    public abstract string RemoveAllActionLabel { get; }

    public ObservableCollection<ActivityItemViewModel> Items { get; } = new();
    public ObservableCollection<BulkActionJobSnapshot> RecentJobs { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private string? _cursor;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ProviderCapability? _capability;
    [ObservableProperty] private bool _selectMode;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isJobActive;
    [ObservableProperty] private BulkActionJobSnapshot? _activeJob;

    public bool CanList => Capability?.CanList == true;
    public bool CanDeleteSelected => Capability?.CanDeleteSelected == true && SelectedCount > 0 && !IsJobActive;
    public bool CanDeleteAll => Capability?.CanDeleteAll == true && Items.Count > 0 && !IsJobActive;
    public bool IsEmpty => HasLoaded && !IsLoading && Items.Count == 0;
    public string? UnsupportedMessage => Capability is { CanList: false } ? (Capability.Notes ?? "This surface is not supported by the current provider.") : null;

    protected ActivitySurfaceViewModelBase(
        IAccountActivityProvider provider,
        IBulkActionJobRunner runner,
        IConfirmationService confirmation,
        IUserPreferenceService preferences)
    {
        Provider = provider;
        Runner = runner;
        Confirmation = confirmation;
        Preferences = preferences;
        Runner.JobUpdated += OnRunnerJobUpdated;
    }

    public async Task EnsureLoadedAsync()
    {
        if (HasLoaded || IsLoading) return;
        await RefreshAsync();
    }

    public virtual void ResetState()
    {
        Items.Clear();
        RecentJobs.Clear();
        IsLoading = false;
        HasLoaded = false;
        Cursor = null;
        HasMore = false;
        ErrorMessage = null;
        Capability = null;
        SelectMode = false;
        SelectedCount = 0;
        IsJobActive = false;
        ActiveJob = null;
        RaiseCommandStates();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            Items.Clear();
            SelectedCount = 0;
            Cursor = null;
            HasMore = false;

            var caps = await Provider.GetCapabilitiesAsync(CancellationToken.None);
            Capability = caps.FirstOrDefault(c => c.Surface == Surface);
            OnPropertyChanged(nameof(CanList));
            OnPropertyChanged(nameof(UnsupportedMessage));

            if (Capability is null || !Capability.CanList)
            {
                HasLoaded = true;
                return;
            }

            await LoadPageAsync();
            HasLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading) return;
        IsLoading = true;
        try
        {
            await LoadPageAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected abstract Task<PagedResult<ActivityItem>> FetchAsync(PageRequest request, CancellationToken cancellationToken);

    private async Task LoadPageAsync()
    {
        var page = await FetchAsync(new PageRequest(GetPageSize(), Cursor), CancellationToken.None);
        foreach (var item in page.Items)
        {
            Items.Add(new ActivityItemViewModel(item, OnItemSelectionChanged));
        }

        Cursor = page.NextCursor;
        HasMore = page.HasMore;
        OnPropertyChanged(nameof(IsEmpty));
        RaiseCommandStates();
    }

    private void OnItemSelectionChanged(ActivityItemViewModel _)
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        RaiseCommandStates();
    }

    [RelayCommand]
    private void ToggleSelectMode()
    {
        SelectMode = !SelectMode;
        if (!SelectMode)
        {
            ClearSelection();
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items)
        {
            if (item.Item.SupportsSelection) item.IsSelected = true;
        }

        SelectedCount = Items.Count(i => i.IsSelected);
        RaiseCommandStates();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items) item.IsSelected = false;
        SelectedCount = 0;
        RaiseCommandStates();
    }

    [RelayCommand]
    public async Task RemoveSelectedAsync()
    {
        if (!CanDeleteSelected) return;

        var selected = Items.Where(i => i.IsSelected && i.Item.SupportsDelete).Select(i => i.Item.Id).ToArray();
        if (selected.Length == 0) return;

        var confirmed = await Confirmation.ConfirmAsync(new ConfirmationRequest(
            Title: $"{RemoveSelectedActionLabel}?",
            Message: $"This action cannot be undone. The selected entries will be removed for your account.",
            ConfirmLabel: RemoveSelectedActionLabel,
            IsDestructive: true,
            AffectedCount: selected.Length));
        if (!confirmed) return;

        await QueueJobAsync(BulkActionType.RemoveSelected, selected, requiresStrong: false, label: RemoveSelectedActionLabel);
    }

    [RelayCommand]
    public async Task RemoveAllAsync()
    {
        if (!CanDeleteAll) return;

        var targets = Items.Where(i => i.Item.SupportsDelete).Select(i => i.Item.Id).ToArray();
        if (targets.Length == 0) return;

        var confirmed = await Confirmation.ConfirmAsync(new ConfirmationRequest(
            Title: $"{RemoveAllActionLabel}?",
            Message: "This is a destructive, irreversible action across every loaded item. Type the confirmation phrase to proceed.",
            ConfirmLabel: RemoveAllActionLabel,
            IsDestructive: true,
            TypedConfirmationText: "DELETE",
            AffectedCount: targets.Length));
        if (!confirmed) return;

        await QueueJobAsync(BulkActionType.RemoveAll, targets, requiresStrong: true, label: RemoveAllActionLabel);
    }

    private async Task QueueJobAsync(BulkActionType actionType, IReadOnlyList<string> targetIds, bool requiresStrong, string label)
    {
        var request = new BulkActionRequest(
            JobId: Guid.NewGuid().ToString("N"),
            ActionType: actionType,
            Surface: Surface,
            TargetIds: targetIds,
            RequestedAt: DateTimeOffset.UtcNow,
            RequiresStrongConfirmation: requiresStrong,
            ActionLabel: label);
        IsJobActive = true;
        RaiseCommandStates();
        await Runner.QueueAsync(request, CancellationToken.None);
    }

    private void OnRunnerJobUpdated(object? sender, BulkActionJobSnapshot snapshot)
    {
        if (snapshot.Surface != Surface) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ActiveJob = snapshot;
            IsJobActive = snapshot.Status is BulkActionJobStatus.Queued or BulkActionJobStatus.Running;

            if (snapshot.SucceededTargetIds.Count > 0)
                RemoveSucceededItems(snapshot.SucceededTargetIds);

            if (!IsJobActive)
            {
                AddToRecent(snapshot);
                OnPropertyChanged(nameof(IsEmpty));
            }

            RaiseCommandStates();
        });
    }

    private void RemoveSucceededItems(IReadOnlyCollection<string> succeededIds)
    {
        var idSet = succeededIds as HashSet<string>
            ?? new HashSet<string>(succeededIds, StringComparer.Ordinal);

        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (idSet.Contains(Items[i].Item.Id))
                Items.RemoveAt(i);
        }

        SelectedCount = Items.Count(i => i.IsSelected);
    }

    private void AddToRecent(BulkActionJobSnapshot snapshot)
    {
        var existing = RecentJobs.FirstOrDefault(j => j.JobId == snapshot.JobId);
        if (existing is not null)
        {
            var index = RecentJobs.IndexOf(existing);
            RecentJobs[index] = snapshot;
        }
        else
        {
            RecentJobs.Insert(0, snapshot);
            while (RecentJobs.Count > 5) RecentJobs.RemoveAt(RecentJobs.Count - 1);
        }
    }

    protected void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanDeleteAll));
        OnPropertyChanged(nameof(IsEmpty));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        RemoveAllCommand.NotifyCanExecuteChanged();
    }

    protected int GetPageSize()
    {
        var configured = Preferences.Current.PageSize;
        return configured switch
        {
            < 10 => 10,
            > 200 => 200,
            _ => configured
        };
    }
}

public sealed partial class ActivityItemViewModel : ObservableObject
{
    private readonly Action<ActivityItemViewModel> _onSelectionChanged;

    public ActivityItem Item { get; }

    [ObservableProperty] private bool _isSelected;

    public ActivityItemViewModel(ActivityItem item, Action<ActivityItemViewModel> onSelectionChanged)
    {
        Item = item;
        _onSelectionChanged = onSelectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged(this);

    public string DisplayTitle => Item.TextSnippet ?? Item.AuthorDisplayName ?? Item.Id;
    public string DisplayAuthor => Item.AuthorDisplayName is { Length: > 0 } name ? name : Item.AuthorHandle ?? "Unknown";
    public string DisplayKind => Item.MediaKind.ToString();
    public string DisplayWhen => Item.OccurredAt is { } when ? when.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : string.Empty;
    public string? PreviewUri => Item.PreviewUri;
    public bool HasRemotePreview => PreviewSurfaceStyle.HasRemoteImage(Item.PreviewUri);
    public IBrush PreviewBrush => PreviewSurfaceStyle.GetBrush(Item.PreviewUri, $"{DisplayTitle}|{DisplayAuthor}");
    public string PreviewMonogram => PreviewSurfaceStyle.GetMonogram(DisplayTitle, DisplayAuthor);
}
