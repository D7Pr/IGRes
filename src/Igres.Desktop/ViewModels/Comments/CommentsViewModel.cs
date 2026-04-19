using Igres.Core.BulkActions;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.ViewModels.Shared;

namespace Igres.Desktop.ViewModels.Comments;

public sealed class CommentsViewModel : ActivitySurfaceViewModelBase
{
    public CommentsViewModel(IAccountActivityProvider provider, IBulkActionJobRunner runner, IConfirmationService confirmation, IUserPreferenceService preferences)
        : base(provider, runner, confirmation, preferences) { }

    public override ActivitySurface Surface => ActivitySurface.Comments;
    public override string Title => "Comments";
    public override string RemoveSelectedActionLabel => "Delete selected";
    public override string RemoveAllActionLabel => "Delete all loaded";

    protected override Task<PagedResult<ActivityItem>> FetchAsync(PageRequest request, CancellationToken cancellationToken)
        => Provider.GetCommentsAsync(request, cancellationToken);
}
