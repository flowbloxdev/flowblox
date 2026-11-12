using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Models.FieldSelection;
using FlowBlox.UICore.Views;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels;
using MahApps.Metro.IconPacks;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckFieldSelectionDialogScrolling(Action<bool, string> check, string directory)
    {
        using var registryScope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        var fields = Enumerable.Range(0, 80)
            .Select(index => new FieldElement { Name = $"Result{index:00}" })
            .Concat(new[]
            {
                new FieldElement { Name = "Input", UserField = true, UserFieldType = UserFieldTypes.Input },
                new FieldElement { Name = "Memory", UserField = true, UserFieldType = UserFieldTypes.Memory }
            })
            .ToArray();
        var dialog = new FieldSelectionWindow(new FieldSelectionWindowArgs
        {
            FieldElements = fields,
            AllowedFieldSelectionModes = [FieldSelectionMode.Fields]
        });
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var list = (ListView)dialog.FindName("FieldsListView");
                var scroll = FindFieldSelectionVisual<ScrollViewer>(list);
                check(scroll != null && scroll.ScrollableHeight > 0,
                    "Field selection list owns a bounded vertical scroll range");
                list.SelectedIndex = 0;
                var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                row.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = Mouse.MouseWheelEvent
                });
                dialog.UpdateLayout();
                check(scroll.VerticalOffset > 0,
                    "Mouse wheel over a field row scrolls the field selection list");
                check(list.SelectedIndex == 0,
                    "Scrolling the field selection list preserves its selection");

                list.ScrollIntoView(list.Items[list.Items.Count - 1]);
                dialog.UpdateLayout();
                var content = (FrameworkElement)dialog.Content;
                var bitmap = new RenderTargetBitmap(
                    (int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, "field-selection-dialog.png"));
                encoder.Save(file);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dialog.Close();
            }
        };
        WpfDialogHost.ShowDialog(dialog, _window);
        if (failure != null)
            throw failure;
    }

    private void CheckFieldSelectionDialogContext(Action<bool, string> check)
    {
        using var registryScope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        var source = new RegexSelectorFlowBlock { Name = "RegexSource" };
        var unconnectedSource = new RegexSelectorFlowBlock { Name = "UnconnectedSource" };
        var connectedField = new FieldElement(source) { Name = "Connected" };
        var requiredField = new FieldElement(unconnectedSource) { Name = "Required" };
        var mapping = new MappingFlowBlock { Name = "Mapping", DefaultValue = requiredField.FullyQualifiedName };
        mapping.ReferencedFlowBlocks.Add(source);
        mapping.RequiredFields.Add(requiredField);
        var entry = new MappingFlowBlockEntry();
        mapping.MappingEntries.Add(entry);
        var args = TextBoxHelper.CreateFieldSelectionWindowArgs(entry, null, mapping);
        check(ReferenceEquals(args.FlowBlock, mapping), "Reactive mapping entry retains its parent flow-block context");
        check(ReferenceEquals(TextBoxHelper.CreateFieldSelectionWindowArgs(mapping, null).FlowBlock, mapping),
            "Direct mapping property retains its own flow-block context");
        args.FieldElements = [connectedField, requiredField];
        args.AllowedFieldSelectionModes = [FieldSelectionMode.Fields];
        var dialog = new FieldSelectionWindow(args);
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var list = (ListView)dialog.FindName("FieldsListView");
                var viewModel = (FieldSelectionWindowViewModel)dialog.DataContext;
                check(viewModel.FieldRows.Single(item => item.FieldElement == connectedField).IsConnected,
                    "Nested field selection marks a referenced source as connected");
                check(!viewModel.FieldRows.Single(item => item.FieldElement == requiredField).IsConnected,
                    "Required fields and inline references alone do not imply a flow connection");
                check(((GridView)list.View).Columns[1].Width == 28,
                    "Connection status uses a narrow column immediately after the field icon");
                var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                check(FindFieldSelectionVisual<PackIconMaterial>(row)?.Kind == PackIconMaterialKind.LinkVariant,
                    "Connected field renders the active link icon");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dialog.Close();
            }
        };
        WpfDialogHost.ShowDialog(dialog, _window);
        if (failure != null)
            throw failure;
    }
}
