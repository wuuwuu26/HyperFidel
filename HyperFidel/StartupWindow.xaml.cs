using Avalonia.Controls;

namespace HyperFidel;

public partial class StartupWindow : Window
{
    public StartupWindow() => InitializeComponent();

    public string Status
    {
        set => StatusTextBlock.Text = value;
    }
}
