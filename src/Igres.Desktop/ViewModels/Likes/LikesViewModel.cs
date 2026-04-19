using Igres.Core.BulkActions;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.ViewModels.Shared;

namespace Igres.Desktop.ViewModels.Likes;

public sealed class LikesViewModel : ActivitySurfaceViewModelBase
{
    public LikesViewModel(IAccountActivityProvider provider, IBulkActionJobRunner runner, IConfirmationService confirmation, IUserPreferenceService preferences)
        : base(provider, runner, confirmation, preferences) { }

    public override ActivitySurface Surface => ActivitySurface.Likes;
    public override string Title => "Likes";
    public override string RemoveSelectedActionLabel => "Unlike selected";
    public override string RemoveAllActionLabel => "Unlike all loaded";

    protected override Task<PagedResult<ActivityItem>> FetchAsync(PageRequest request, CancellationToken cancellationToken)
        => Provider.GetLikesAsync(request, cancellationToken);
}
