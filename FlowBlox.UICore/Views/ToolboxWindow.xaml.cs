using FlowBlox.UICore.Models.Toolbox;
using FlowBlox.UICore.ViewModels;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace FlowBlox.UICore.Views
{
    /// <summary>
    /// Interaktionslogik für ToolboxWindow.xaml
    /// </summary>
    public partial class ToolboxWindow : MetroWindow
    {
        public ToolboxWindowViewModel ViewModel { get; private set; }

        public ToolboxWindow(bool isSelectionMode = false, string? passedToolboxCategory = null)
        {
            InitializeComponent();
            ViewModel = new ToolboxWindowViewModel(isSelectionMode, passedToolboxCategory);
            DataContext = ViewModel;
            ViewModel.RequestClose += (s, e) => this.DialogResult = true;
            ViewModel.ShowNotification += async (s, e) => await this.ShowMessageAsync("Info", e.Message, MessageDialogStyle.Affirmative, new MetroDialogSettings()
            {
                ColorScheme = MetroDialogColorScheme.Accented
            });
        }

        public ToolboxElement SelectedToolboxElement => ViewModel.SelectedToolboxElement;

        private void Back_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ListViewItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel.SelectCommand.Execute(null);
        }
    }
}
