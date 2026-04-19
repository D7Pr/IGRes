using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Igres.Desktop.Views.SavedCollections;

public partial class SavedCollectionsView : UserControl
{
    public SavedCollectionsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
