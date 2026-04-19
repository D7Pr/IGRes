using Igres.Core.BulkActions;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.ViewModels.Shared;

namespace Igres.Desktop.ViewModels.Reposts;

public sealed class RepostsViewModel : ActivitySurfaceViewModelBase
{
    public RepostsViewModel(IAccountActivityProvider provider, IBulkActionJobRunner runner, IConfirmationService confirmation, IUserPreferenceService preferences)
        : base(provider, runner, confirmation, preferences) { }

    public override ActivitySurface Surface => ActivitySurface.Reposts;
    public override string Title => "Reposts";
    public override string RemoveSelectedActionLabel => "Remove selected";
    public override string RemoveAllActionLabel => "Remove all loaded";

    protected override Task<PagedResult<ActivityItem>> FetchAsync(PageRequest request, CancellationToken cancellationToken)
        => Provider.GetRepostsAsync(request, cancellationToken);
}
