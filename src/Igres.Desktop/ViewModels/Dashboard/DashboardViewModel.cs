using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Igres.Core.Models;
using Igres.Core.Providers;

namespace Igres.Desktop.ViewModels.Dashboard;

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IAccountActivityProvider _provider;

    public ObservableCollection<SurfaceSummary> Summaries { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private string? _errorMessage;

    public DashboardViewModel(IAccountActivityProvider provider)
    {
        _provider = provider;
    }

    public void ResetState()
    {
        Summaries.Clear();
        IsLoading = false;
        HasLoaded = false;
        ErrorMessage = null;
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
        ErrorMessage = null;
        Summaries.Clear();
        try
        {
            var caps = await _provider.GetCapabilitiesAsync(CancellationToken.None);
            var labels = new (ActivitySurface Surface, string Label)[]
            {
                (ActivitySurface.SavedItems, "Saved items"),
                (ActivitySurface.SavedCollections, "Saved collections"),
                (ActivitySurface.Reposts, "Reposts"),
                (ActivitySurface.Likes, "Likes"),
                (ActivitySurface.Comments, "Comments")
            };

            foreach (var p in labels)
            {
                var cap = caps.FirstOrDefault(c => c.Surface == p.Surface);
                if (cap is null || !cap.CanList)
                {
                    Summaries.Add(new SurfaceSummary(p.Label, p.Surface, null, cap));
                    continue;
                }

                try
                {
                    var count = await CountAsync(p.Surface);
                    Summaries.Add(new SurfaceSummary(p.Label, p.Surface, count, cap));
                }
                catch
                {
                    Summaries.Add(new SurfaceSummary(p.Label, p.Surface, null, cap));
                }
            }

            HasLoaded = true;
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

    private async Task<int> CountAsync(ActivitySurface surface)
    {
        const int overviewPageSize = 1000;
        var total = 0;
        string? cursor = null;
        do
        {
            if (surface == ActivitySurface.SavedCollections)
            {
                var page = await _provider.GetSavedCollectionsAsync(new PageRequest(overviewPageSize, cursor), CancellationToken.None);
                total += page.Items.Count;
                cursor = page.NextCursor;
                if (!page.HasMore)
                    break;
            }
            else
            {
                var page = surface switch
                {
                    ActivitySurface.SavedItems => await _provider.GetSavedItemsAsync(new PageRequest(overviewPageSize, cursor), CancellationToken.None),
                    ActivitySurface.Reposts => await _provider.GetRepostsAsync(new PageRequest(overviewPageSize, cursor), CancellationToken.None),
                    ActivitySurface.Likes => await _provider.GetLikesAsync(new PageRequest(overviewPageSize, cursor), CancellationToken.None),
                    ActivitySurface.Comments => await _provider.GetCommentsAsync(new PageRequest(overviewPageSize, cursor), CancellationToken.None),
                    _ => new PagedResult<ActivityItem>(Array.Empty<ActivityItem>(), null, false)
                };
                total += page.Items.Count;
                cursor = page.NextCursor;
                if (!page.HasMore)
                    break;
            }
        } while (cursor is not null && total < 1000);

        return total;
    }
}

public sealed record SurfaceSummary(string Label, ActivitySurface Surface, int? Count, ProviderCapability? Capability)
{
    public string CountLabel => Count?.ToString(CultureInfo.InvariantCulture) ?? "-";

    public string CapabilityLabel
    {
        get
        {
            if (Capability is null)
                return "Unsupported";
            if (!Capability.CanList)
                return "Unsupported";

            var bits = new List<string>();
            if (Capability.CanDeleteSelected)
                bits.Add("delete selected");
            if (Capability.CanDeleteAll)
                bits.Add("delete all");
            if (Capability.CanDeleteCollection)
                bits.Add("delete collection");

            return bits.Count == 0 ? "Review only" : string.Join(", ", bits);
        }
    }
}
