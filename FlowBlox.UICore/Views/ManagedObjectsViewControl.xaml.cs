using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.ViewModels.ManagedObjects;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FlowBlox.UICore.Views
{
    public partial class ManagedObjectsViewControl : UserControl
    {
        public ManagedObjectsViewControl()
        {
            InitializeComponent();
            DataContextChanged += ManagedObjectsViewControl_DataContextChanged;
            Loaded += ManagedObjectsViewControl_Loaded;
            Unloaded += ManagedObjectsViewControl_Unloaded;
        }

        public void AfterProjectFullyInitialized()
        {
            if (DataContext is ManagedObjectsViewModel vm)
                vm.AfterProjectFullyInitialized();
        }

        private void ManagedObjectsViewControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ManagedObjectsViewModel vm)
                vm.AfterProjectFullyInitialized();
        }

        private void ManagedObjectsViewControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ManagedObjectsViewModel oldVm)
                oldVm.Dispose();
        }

        private void ManagedObjectsViewControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ManagedObjectsViewModel vm)
                vm.Dispose();
        }

        private void TypeTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not ManagedObjectsViewModel vm)
                return;

            if (e.NewValue is ManagedObjectTypeNodeViewModel node)
                vm.SelectedTypeNode = node;
        }

        private void ManagedObjectsListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView listView)
                return;

            var current = e.OriginalSource as DependencyObject;
            while (current != null && current is not ListViewItem)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is ListViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void ManagedObjectsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var current = e.OriginalSource as DependencyObject;
            while (current != null && current is not ListViewItem)
                current = VisualTreeHelper.GetParent(current);

            if (current is not ListViewItem || DataContext is not ManagedObjectsViewModel vm)
                return;

            if (vm.EditCommand.CanExecute(null))
                vm.EditCommand.Execute(null);
        }

        private void ActionsDropDownButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }

        private void ActionsDropDownMenu_Click(object sender, RoutedEventArgs e)
        {
            ((ContextMenu)sender).IsOpen = false;
        }
    }
}
