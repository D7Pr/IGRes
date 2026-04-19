using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Igres.Desktop.Views.Shell;

public partial class ShellView : Window
{
    public ShellView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
