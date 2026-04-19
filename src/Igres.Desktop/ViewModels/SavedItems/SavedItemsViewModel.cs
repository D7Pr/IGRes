using Igres.Core.BulkActions;
using Igres.Core.Models;
using Igres.Core.Providers;
using Igres.Core.Services;
using Igres.Desktop.ViewModels.Shared;

namespace Igres.Desktop.ViewModels.SavedItems;

public sealed class SavedItemsViewModel : ActivitySurfaceViewModelBase
{
    public SavedItemsViewModel(IAccountActivityProvider provider, IBulkActionJobRunner runner, IConfirmationService confirmation, IUserPreferenceService preferences)
        : base(provider, runner, confirmation, preferences) { }

    public override ActivitySurface Surface => ActivitySurface.SavedItems;
    public override string Title => "Saved items";
    public override string RemoveSelectedActionLabel => "Remove selected";
    public override string RemoveAllActionLabel => "Remove all saved";

    protected override Task<PagedResult<ActivityItem>> FetchAsync(PageRequest request, CancellationToken cancellationToken)
        => Provider.GetSavedItemsAsync(request, cancellationToken);
}
