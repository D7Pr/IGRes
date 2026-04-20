using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.Services;

namespace Igres.Desktop.ViewModels.SavedCollections;

public sealed partial class SavedCollectionsViewModel : ViewModelBase
{
    private readonly IAccountActivityProvider _provider;
    private readonly CollectionCleanupService _cleanup;
    private readonly IConfirmationService _confirmation;
    private readonly IUserPreferenceService _preferences;

    public ObservableCollection<SavedCollectionCardViewModel> Collections { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ProviderCapability? _capability;
    [ObservableProperty] private CollectionDetailViewModel? _openDetail;
    [ObservableProperty] private string? _statusMessage;

    public bool CanList => Capability?.CanList == true;
    public string? UnsupportedMessage => Capability is { CanList: false } ? (Capability.Notes ?? "Saved collections are not supported by this provider.") : null;
    public bool IsEmpty => HasLoaded && !IsLoading && Collections.Count == 0;
    public bool IsInDetail => OpenDetail is not null;
    public bool IsInList => OpenDetail is null;

    public SavedCollectionsViewModel(IAccountActivityProvider provider, CollectionCleanupService cleanup, IConfirmationService confirmation, IUserPreferenceService preferences)
    {
        _provider = provider;
        _cleanup = cleanup;
        _confirmation = confirmation;
        _preferences = preferences;
    }

    public async Task EnsureLoadedAsync()
    {
        if (HasLoaded || IsLoading) return;
        await RefreshAsync();
    }

    public void ResetState()
    {
        Collections.Clear();
        IsLoading = false;
        HasLoaded = false;
        ErrorMessage = null;
        Capability = null;
        OpenDetail = null;
        StatusMessage = null;
        OnPropertyChanged(nameof(CanList));
        OnPropertyChanged(nameof(UnsupportedMessage));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsInDetail));
        OnPropertyChanged(nameof(IsInList));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var caps = await _provider.GetCapabilitiesAsync(CancellationToken.None);
            Capability = caps.FirstOrDefault(c => c.Surface == ActivitySurface.SavedCollections);
            OnPropertyChanged(nameof(CanList));
            OnPropertyChanged(nameof(UnsupportedMessage));
            Collections.Clear();
            if (Capability is null || !Capability.CanList)
            {
                HasLoaded = true;
                return;
            }
            var pageSize = GetPageSize();
            string? cursor = null;
            while (true)
            {
                var page = await _provider.GetSavedCollectionsAsync(new PageRequest(pageSize, cursor), CancellationToken.None);
                foreach (var c in page.Items)
                {
                    Collections.Add(new SavedCollectionCardViewModel(c));
                }
                cursor = page.NextCursor;
                if (!page.HasMore) break;
            }
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
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

    [RelayCommand]
    public async Task OpenCollectionAsync(SavedCollectionCardViewModel card)
    {
        if (card is null) return;
        var detail = new CollectionDetailViewModel(_provider, _cleanup, _confirmation, _preferences, card.Collection);
        detail.CloseRequested += OnDetailClosed;
        detail.CollectionItemsRemoved += OnCollectionItemsRemoved;
        OpenDetail = detail;
        OnPropertyChanged(nameof(IsInDetail));
        OnPropertyChanged(nameof(IsInList));
        await detail.LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteCollectionAsync(SavedCollectionCardViewModel card)
    {
        if (card is null || !card.Collection.SupportsDelete) return;
        var typed = card.Collection.Name;
        var confirmed = await _confirmation.ConfirmAsync(new ConfirmationRequest(
            Title: $"Delete collection \"{card.Collection.Name}\"?",
            Message: "Deleting a collection removes the collection and its relationship to your saved items. This cannot be undone.",
            ConfirmLabel: "Delete collection",
            IsDestructive: true,
            TypedConfirmationText: typed,
            AffectedCount: card.Collection.ItemCount));
        if (!confirmed) return;
        try
        {
            await _cleanup.DeleteCollectionAsync(card.Collection, CancellationToken.None);
            Collections.Remove(card);
            OnPropertyChanged(nameof(IsEmpty));
            StatusMessage = $"Delete job queued for \"{card.Collection.Name}\".";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e)
    {
        OpenDetail = null;
        OnPropertyChanged(nameof(IsInDetail));
        OnPropertyChanged(nameof(IsInList));
    }

    private void OnCollectionItemsRemoved(object? sender, (string CollectionId, int NewCount) e)
    {
        var card = Collections.FirstOrDefault(c => c.Collection.CollectionId == e.CollectionId);
        if (card is not null)
        {
            card.UpdateCount(e.NewCount);
        }
    }

    private int GetPageSize()
    {
        var configured = _preferences.Current.PageSize;
        return configured switch
        {
            < 10 => 10,
            > 200 => 200,
            _ => configured
        };
    }
}

public sealed partial class SavedCollectionCardViewModel : ObservableObject
{
    [ObservableProperty] private SavedCollection _collection;

    public SavedCollectionCardViewModel(SavedCollection collection)
    {
        _collection = collection;
    }

    public void UpdateCount(int newCount)
    {
        Collection = Collection with { ItemCount = newCount };
    }

    partial void OnCollectionChanged(SavedCollection value)
    {
        OnPropertyChanged(nameof(PreviewA));
        OnPropertyChanged(nameof(PreviewB));
        OnPropertyChanged(nameof(PreviewC));
        OnPropertyChanged(nameof(HasPreviewAImage));
        OnPropertyChanged(nameof(HasPreviewBImage));
        OnPropertyChanged(nameof(HasPreviewCImage));
        OnPropertyChanged(nameof(PreviewABrush));
        OnPropertyChanged(nameof(PreviewBBrush));
        OnPropertyChanged(nameof(PreviewCBrush));
        OnPropertyChanged(nameof(PreviewMonogram));
        OnPropertyChanged(nameof(UpdatedLabel));
        OnPropertyChanged(nameof(SupportsDelete));
        OnPropertyChanged(nameof(SupportLabel));
    }

    public string PreviewA => Collection.PreviewUris.ElementAtOrDefault(0) ?? string.Empty;
    public string PreviewB => Collection.PreviewUris.ElementAtOrDefault(1) ?? string.Empty;
    public string PreviewC => Collection.PreviewUris.ElementAtOrDefault(2) ?? string.Empty;
    public bool HasPreviewAImage => PreviewSurfaceStyle.HasRemoteImage(PreviewA);
    public bool HasPreviewBImage => PreviewSurfaceStyle.HasRemoteImage(PreviewB);
    public bool HasPreviewCImage => PreviewSurfaceStyle.HasRemoteImage(PreviewC);
    public IBrush PreviewABrush => PreviewSurfaceStyle.GetBrush(PreviewA, $"{Collection.Name}|A");
    public IBrush PreviewBBrush => PreviewSurfaceStyle.GetBrush(PreviewB, $"{Collection.Name}|B");
    public IBrush PreviewCBrush => PreviewSurfaceStyle.GetBrush(PreviewC, $"{Collection.Name}|C");
    public string PreviewMonogram => PreviewSurfaceStyle.GetMonogram(Collection.Name);
    public string UpdatedLabel => Collection.LastUpdatedAt is { } t ? $"Updated {t.ToLocalTime():MMM d, yyyy}" : "Never updated";
    public bool SupportsDelete => Collection.SupportsDelete;
    public string SupportLabel => Collection.SupportsDelete ? "Deletion supported" : "Deletion unsupported";
}
