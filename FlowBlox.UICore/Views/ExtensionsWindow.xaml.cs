using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.ViewModels;
using MahApps.Metro.Controls;
using System.Windows.Input;

namespace FlowBlox.UICore.Views
{
    /// <summary>
    /// Interaktionslogik für ExtensionsWindow.xaml
    /// </summary>
    public partial class ExtensionsWindow : MetroWindow
    {
        public ExtensionsWindow(
            FlowBloxProject project = null,
            Action<FlowBloxProject> prepareProjectForSave = null,
            Action<FlowBloxProject> restoreProjectAfterSaveAttempt = null,
            bool loadRemoteData = true)
        {
            InitializeComponent();
            DataContext = new ExtensionsViewModel(
                this,
                project,
                prepareProjectForSave,
                restoreProjectAfterSaveAttempt,
                loadRemoteData);
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (SearchButton.IsEnabled)
                {
                    if (SearchButton.Command != null && SearchButton.Command.CanExecute(null))
                        SearchButton.Command.Execute(null);
                }
                e.Handled = true;
            }
        }
    }
}
