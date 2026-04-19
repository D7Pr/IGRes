using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.BulkActions;
using Igres.Core.Models;

namespace Igres.Desktop.ViewModels.Jobs;

public sealed partial class JobsViewModel : ViewModelBase
{
    private readonly IBulkActionJobRunner _runner;

    public ObservableCollection<JobRowViewModel> Jobs { get; } = new();

    [ObservableProperty] private JobRowViewModel? _selectedJob;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasLoaded;

    public JobsViewModel(IBulkActionJobRunner runner)
    {
        _runner = runner;
        _runner.JobUpdated += OnJobUpdated;
    }

    public async Task EnsureLoadedAsync()
    {
        if (HasLoaded || IsLoading)
            return;

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            var recent = await _runner.GetRecentJobsAsync(CancellationToken.None);
            var recentIds = recent.Select(r => r.JobId).ToHashSet();
            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                if (!recentIds.Contains(Jobs[i].JobId))
                {
                    Jobs.RemoveAt(i);
                }
            }

            var snapshotsByJob = Jobs.ToDictionary(j => j.JobId);
            foreach (var snap in recent)
            {
                if (!snapshotsByJob.ContainsKey(snap.JobId))
                {
                    Jobs.Add(new JobRowViewModel(snap));
                }
            }

            HasLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ResetState()
    {
        Jobs.Clear();
        SelectedJob = null;
        IsLoading = false;
        HasLoaded = false;
    }

    [RelayCommand]
    private async Task CancelAsync(JobRowViewModel? row)
    {
        if (row is null || !row.CanCancel) return;
        await _runner.CancelAsync(row.JobId, CancellationToken.None);
    }

    private void OnJobUpdated(object? sender, BulkActionJobSnapshot snapshot)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var existing = Jobs.FirstOrDefault(j => j.JobId == snapshot.JobId);
            if (existing is null)
            {
                Jobs.Insert(0, new JobRowViewModel(snapshot));
            }
            else
            {
                existing.Apply(snapshot);
                if (SelectedJob == existing)
                {
                    OnPropertyChanged(nameof(SelectedJob));
                }
            }
        });
    }
}

public sealed partial class JobRowViewModel : ObservableObject
{
    [ObservableProperty] private BulkActionJobSnapshot _snapshot;

    public JobRowViewModel(BulkActionJobSnapshot snapshot) => _snapshot = snapshot;

    public string JobId => Snapshot.JobId;
    public string Label => Snapshot.ActionLabel;
    public string Surface => Snapshot.Surface.ToString();
    public BulkActionJobStatus Status => Snapshot.Status;
    public string StatusLabel => Snapshot.Status.ToString();
    public int Total => Snapshot.TotalCount;
    public int Processed => Snapshot.ProcessedCount;
    public int Succeeded => Snapshot.SucceededCount;
    public int Failed => Snapshot.FailedCount;
    public int Skipped => Snapshot.SkippedCount;
    public int Canceled => Snapshot.CanceledCount;
    public double ProgressPercent => Snapshot.TotalCount == 0 ? 0 : Math.Min(100.0, Snapshot.ProcessedCount * 100.0 / Snapshot.TotalCount);
    public bool CanCancel => Snapshot.CanCancel;
    public IReadOnlyList<BulkActionResultItem> Results => Snapshot.RecentResults;
    public string Timing =>
        Snapshot.StartedAt is null ? "Queued"
            : Snapshot.CompletedAt is { } end ? $"Completed {end.ToLocalTime():HH:mm:ss}"
            : $"Started {Snapshot.StartedAt.Value.ToLocalTime():HH:mm:ss}";

    public void Apply(BulkActionJobSnapshot snap)
    {
        Snapshot = snap;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Processed));
        OnPropertyChanged(nameof(Succeeded));
        OnPropertyChanged(nameof(Failed));
        OnPropertyChanged(nameof(Skipped));
        OnPropertyChanged(nameof(Canceled));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(Results));
        OnPropertyChanged(nameof(Timing));
    }
}
