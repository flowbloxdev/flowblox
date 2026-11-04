using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckContextMenusAsync(string directory, Action<bool, string> check)
    {
        _window.ActivatePanel(ProjectPanelView);
        await Task.Delay(100);
        var projectControl = ProjectPanelView.WpfControl;
        var node = projectControl.ViewModel.Nodes.First(item => !item.IsNote);
        projectControl.ViewModel.SelectNode(node, toggle: false, extend: false);
        projectControl.UpdateLayout();
        var nodeSurface = MenuVisuals<Border>(projectControl).First(border =>
            ReferenceEquals(border.DataContext, node) && border.ContextMenu != null);
        var projectMenu = nodeSurface.ContextMenu;

        try
        {
            await OpenMenu(nodeSurface, projectMenu, "project-context-menu.png");
            var entries = projectMenu.Items.OfType<MenuItem>().ToList();
            check(entries.Any(item => !item.IsEnabled), "Project context menu preserves disabled commands");
            check(entries.Any(item => !string.IsNullOrEmpty(item.InputGestureText)), "Project context menu preserves shortcuts");

            var submenu = entries.First(item => item.HasItems && item.IsEnabled);
            var submenuPeer = new MenuItemAutomationPeer(submenu);
            var submenuAutomation = (IExpandCollapseProvider)submenuPeer.GetPattern(PatternInterface.ExpandCollapse);
            submenuAutomation.Expand();
            await Task.Delay(100);
            check(submenu.IsSubmenuOpen, "Context submenu opens through UI automation");
            var popup = (Popup)submenu.Template.FindName("PART_Popup", submenu);
            check(popup.IsOpen && popup.Child.IsVisible, "Context submenu has a visible popup surface");
            SaveMenuImage(directory, (FrameworkElement)popup.Child, "project-context-submenu.png");
            submenuAutomation.Collapse();
            check(!submenu.IsSubmenuOpen, "Context submenu closes through UI automation");

            var breakpoint = entries.Single(item => ReferenceEquals(item.Command, projectControl.ViewModel.ToggleBreakpointCommand));
            var wasBreakpoint = node.InternalFlowBlock.BreakPoint;
            var peer = new MenuItemAutomationPeer(breakpoint);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            check(node.InternalFlowBlock.BreakPoint != wasBreakpoint,
                "Styled project context command executes through UI automation");
            projectControl.ViewModel.ToggleBreakpointCommand.Execute(null);
        }
        finally
        {
            projectMenu.IsOpen = false;
        }

        _window.ActivatePanel(ManagedObjectsPanel);
        _window.UpdateLayout();
        await Task.Delay(250);
        var managedList = (ListView)ManagedObjectsPanel.WpfContent.FindName("ManagedObjectsListView");
        try
        {
            await OpenMenu(managedList, managedList.ContextMenu, "managed-objects-context-menu.png");
            var actions = managedList.ContextMenu.Items.OfType<MenuItem>()
                .FirstOrDefault(item => item.HasItems && item.IsEnabled);
            if (actions != null)
            {
                var peer = new MenuItemAutomationPeer(actions);
                var automation = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
                automation.Expand();
                await Task.Delay(100);
                var popup = (Popup)actions.Template.FindName("PART_Popup", actions);
                var action = (MenuItem)actions.ItemContainerGenerator.ContainerFromIndex(0);
                check(action != null && action.Template.FindName("Row", action) is Border,
                    "Generated managed-object action items retain the shared menu template");
                SaveMenuImage(directory, (FrameworkElement)popup.Child, "managed-object-actions-submenu.png");
                automation.Collapse();
            }
        }
        finally
        {
            managedList.ContextMenu.IsOpen = false;
        }

        _window.ActivatePanel(FieldPanel);
        _window.UpdateLayout();
        await Task.Delay(250);
        var fieldList = (ListView)FieldPanel.WpfContent.FindName("FieldListView");
        if (fieldList.Items.Count > 0)
            fieldList.SelectedIndex = 0;
        try
        {
            await OpenMenu(fieldList, fieldList.ContextMenu, "field-context-menu.png");
        }
        finally
        {
            fieldList.ContextMenu.IsOpen = false;
        }

        async Task OpenMenu(FrameworkElement target, ContextMenu menu, string fileName)
        {
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Center;
            menu.IsOpen = true;
            await Task.Delay(100);
            menu.UpdateLayout();
            check(menu.IsVisible && menu.ActualWidth > 0,
                $"{fileName} opens inside the real host (open: {menu.IsOpen}, target: {target.IsLoaded}/{target.IsVisible}, size: {target.ActualWidth}x{target.ActualHeight})");
            var surface = menu.Template.FindName("MenuSurface", menu) as Border;
            check(surface != null && surface.CornerRadius.TopLeft > 0 && surface.Background != null,
                fileName + " resolves the shared rounded surface and theme");
            var row = menu.Items.OfType<MenuItem>().First();
            check(row.Template.FindName("Row", row) is Border && row.FontSize == 12,
                fileName + " uses the shared compact item template");
            SaveMenuImage(directory, menu, fileName);
        }
    }

    private static void SaveMenuImage(string directory, FrameworkElement element, string fileName)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }

    private static IEnumerable<T> MenuVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in MenuVisuals<T>(child))
                yield return descendant;
        }
    }
}
