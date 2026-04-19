using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Igres.Desktop.Views.Likes;

public partial class LikesView : UserControl
{
    public LikesView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
