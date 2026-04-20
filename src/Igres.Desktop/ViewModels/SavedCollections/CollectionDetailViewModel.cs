using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.ViewModels.Shared;

namespace Igres.Desktop.ViewModels.SavedCollections;

public sealed partial class CollectionDetailViewModel : ViewModelBase
{
    private readonly IAccountActivityProvider _provider;
    private readonly CollectionCleanupService _cleanup;
    private readonly IConfirmationService _confirmation;
    private readonly IUserPreferenceService _preferences;

    public event EventHandler? CloseRequested;
    public event EventHandler<(string CollectionId, int NewCount)>? CollectionItemsRemoved;

    public SavedCollection Collection { get; }
    public ObservableCollection<ActivityItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isJobActive;

    public bool CanRemoveSelected => Collection.SupportsItemRemoval && SelectedCount > 0 && !IsJobActive;
    public string Title => Collection.Name;

    public CollectionDetailViewModel(IAccountActivityProvider provider, CollectionCleanupService cleanup, IConfirmationService confirmation, IUserPreferenceService preferences, SavedCollection collection)
    {
        _provider = provider;
        _cleanup = cleanup;
        _confirmation = confirmation;
        _preferences = preferences;
        Collection = collection;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Items.Clear();
            SelectedCount = 0;
            var pageSize = GetPageSize();
            string? cursor = null;
            while (true)
            {
                var page = await _provider.GetCollectionItemsAsync(Collection.CollectionId, new PageRequest(pageSize, cursor), CancellationToken.None);
                foreach (var item in page.Items)
                {
                    Items.Add(new ActivityItemViewModel(item, OnItemSelectionChanged));
                }
                cursor = page.NextCursor;
                if (!page.HasMore) break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    private void OnItemSelectionChanged(ActivityItemViewModel _)
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        Notify();
    }

    [RelayCommand]
    public async Task RemoveSelectedAsync()
    {
        if (!CanRemoveSelected) return;
        var ids = Items.Where(i => i.IsSelected).Select(i => i.Item.Id).ToArray();
        if (ids.Length == 0) return;
        var confirmed = await _confirmation.ConfirmAsync(new ConfirmationRequest(
            Title: "Remove from collection?",
            Message: $"Selected entries will be removed from \"{Collection.Name}\" but will remain saved on your account.",
            ConfirmLabel: "Remove from collection",
            IsDestructive: true,
            AffectedCount: ids.Length));
        if (!confirmed) return;
        IsJobActive = true;
        Notify();
        try
        {
            await _cleanup.RemoveSelectedItemsAsync(Collection.CollectionId, ids, CancellationToken.None);
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (ids.Contains(Items[i].Item.Id)) Items.RemoveAt(i);
            }
            SelectedCount = Items.Count(i => i.IsSelected);
            CollectionItemsRemoved?.Invoke(this, (Collection.CollectionId, Items.Count));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsJobActive = false;
            Notify();
        }
    }

    [RelayCommand]
    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Notify()
    {
        OnPropertyChanged(nameof(CanRemoveSelected));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
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
