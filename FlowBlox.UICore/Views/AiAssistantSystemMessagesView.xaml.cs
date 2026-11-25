using System.Windows;
using System.Windows.Controls;

namespace FlowBlox.UICore.Views;

public partial class AiAssistantSystemMessagesView : UserControl
{
    public AiAssistantSystemMessagesView() => InitializeComponent();

    private void OpenFilterMenu(object sender, RoutedEventArgs args)
    {
        var button = sender as Button;
        var menu = button?.ContextMenu;
        if (menu == null)
            return;

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}
