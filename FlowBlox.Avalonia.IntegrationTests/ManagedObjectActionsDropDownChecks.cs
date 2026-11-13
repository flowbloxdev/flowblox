using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.ViewModels.ManagedObjects;
using FlowBlox.UICore.ViewModels.PropertyWindow;
using FlowBlox.UICore.Views;
using MahApps.Metro.IconPacks;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckManagedObjectActionsDropDownAsync(string directory, Action<bool, string> check)
    {
        var control = new ManagedObjectsViewControl();
        var window = new Window { Content = control, Width = 1000, Height = 350, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Task.Delay(150);
            var model = (ManagedObjectsViewModel)control.DataContext;
            var button = (Button)control.FindName("ActionsDropDownButton");
            check(!button.IsEnabled, "Managed-object Actions dropdown is disabled without available actions");
            var invoked = false;
            var entry = new ManagedObjectEntryViewModel(null, null) { Name = "FileObject0" };
            entry.Actions.Add(new UIActionViewModel
            {
                DisplayName = "Open",
                Icon = WpfIconHelper.CreateMaterialIcon(PackIconMaterialKind.FileEditOutline, 16),
                Command = new RelayCommand(() => invoked = true)
            });
            model.ManagedObjects.Add(entry);
            model.SelectedEntry = entry;
            window.UpdateLayout();
            check(button.IsEnabled, "Selecting a managed object enables its available Actions dropdown");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            var menu = button.ContextMenu;
            check(menu.IsOpen && ReferenceEquals(menu.PlacementTarget, button) && menu.Items.Count == 1,
                "Managed-object Actions opens beneath its toolbar button with the selected object's actions");
            var item = (MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(0);
            check(item.Command != null && item.Command.CanExecute(null),
                "Managed-object dropdown action binds its executable command");
            SaveMenuImage(directory, control, "managed-object-actions-toolbar.png");
            SaveMenuImage(directory, menu, "managed-object-actions-dropdown.png");
            var peer = new MenuItemAutomationPeer(item);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            check(invoked, "Choosing a managed-object action invokes its command");
            check(!menu.IsOpen, "Choosing a managed-object action closes the dropdown");
        }
        finally
        {
            window.Close();
        }
    }
}
