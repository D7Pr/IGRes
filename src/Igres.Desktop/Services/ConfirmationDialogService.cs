using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Igres.Core.Services;
using Igres.Desktop.Views.Dialogs;

namespace Igres.Desktop.Services;

public sealed class ConfirmationDialogService : IConfirmationService
{
    public async Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = GetMainWindow();
            if (owner is null) return false;
            var dialog = new ConfirmationDialog
            {
                DataContext = new ConfirmationDialogViewModel(request)
            };
            var result = await dialog.ShowDialog<bool?>(owner);
            return result == true;
        });
    }

    private static Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
