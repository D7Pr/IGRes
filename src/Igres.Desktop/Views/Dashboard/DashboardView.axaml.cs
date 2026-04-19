using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Igres.Desktop.Views.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
