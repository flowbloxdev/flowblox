using FlowBlox.UICore.ViewModels;
using MahApps.Metro.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.UICore.Views
{
    public partial class PSProjectsWindow : MetroWindow
    {
        private readonly ProjectSpaceFavorites _favorites;
        private void FavoriteButton_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }
        private void FavoriteButton_Loaded(object sender, RoutedEventArgs e)
        {
            var button = (ToggleButton)sender;
            if (button.DataContext is not FbProjectResult project)
                return;
            try
            {
                button.IsChecked = _favorites.Contains(ProjectShortcut.CurrentApiEndpoint, project.Guid);
            }
            catch (Exception exception)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to read Project Space favorites.", exception);
            }
        }

        private void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var button = (ToggleButton)sender;
            if (button.DataContext is not FbProjectResult project)
                return;
            try
            {
                _favorites.SetFavorite(
                    ProjectShortcut.CurrentApiEndpoint, project.Guid, project.Name, button.IsChecked == true);
            }
            catch (Exception exception)
            {
                button.IsChecked = button.IsChecked != true;
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to save a Project Space favorite.", exception);
                MessageBox.Show(this, string.Format(Texts.Error, exception.Message), Texts.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public PSProjectsWindow() : this(loadRemoteData: true)
        {
        }

        public PSProjectsWindow(bool loadRemoteData, ProjectSpaceFavorites? favorites = null)
        {
            InitializeComponent();
            _favorites = favorites ?? ProjectSpaceFavorites.Instance;
            DataContext = new PSProjectsViewModel(this, loadRemoteData);
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is PSProjectsViewModel vm)
                vm.SearchCommand.Execute(null);
        }

        private void ProjectCards_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var list = sender as ListBox;
            var source = e.OriginalSource as DependencyObject;
            if (list == null || source == null)
                return;

            if (ItemsControl.ContainerFromElement(list, source) is ListBoxItem)
                e.Handled = TryOpenSelectedProject();
        }

        private void ProjectCards_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                e.Handled = TryOpenSelectedProject();
        }

        private bool TryOpenSelectedProject()
        {
            var viewModel = DataContext as PSProjectsViewModel;
            if (viewModel == null || !viewModel.OpenProjectCommand.CanExecute(null))
                return false;

            viewModel.OpenProjectCommand.Execute(null);
            return true;
        }
    }
}
