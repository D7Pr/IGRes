using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;

namespace Igres.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    protected static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
