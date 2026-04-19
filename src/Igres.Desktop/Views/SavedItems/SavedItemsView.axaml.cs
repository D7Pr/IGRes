using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Igres.Desktop.Views.SavedItems;

public partial class SavedItemsView : UserControl
{
    public SavedItemsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
