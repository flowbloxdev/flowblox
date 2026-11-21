using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckExtensionCards(Action<bool, string> check, string directory)
    {
        var dialog = new ExtensionsWindow(loadRemoteData: false);
        var model = (ExtensionsViewModel)dialog.DataContext;
        model.SearchResults = Enumerable.Range(1, 35).Select(index => new FbExtensionResult
        {
            Guid = Guid.NewGuid(),
            Name = $"Extension {index}: reusable data tools",
            Description = "Reusable connectors and FlowBlocks with a bounded card description and version details.",
            DownloadCount = index * 10_000,
            Relevance = index == 1 ? 1_000_000 : null,
            UpdatedAt = new DateTime(2026, 11, 20),
            Versions = [new FbVersionResult { Version = "1.2.0", RuntimeVersion = "1.0.8", Changes = "Extension release notes", DownloadCount = 3_456 }]
        }).ToList();
        check(model.SearchResults[0].DownloadCount == 10_000 && model.SearchResults[1].DownloadCount == 350_000,
            "Extension cards prefer API relevance and fall back to downloads on older servers");
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var cards = (ListBox)dialog.FindName("ExtensionCards");
                cards.SelectedIndex = 1;
                dialog.UpdateLayout();
                check(ReferenceEquals(model.SelectedExtension, cards.SelectedItem) && model.SelectedVersion.Version == "1.2.0",
                    "Extension card selection preserves the version selector and release details");
                var scroll = FindFieldSelectionVisual<ScrollViewer>(cards);
                check(scroll != null && scroll.ScrollableHeight > 0,
                    "Extension cards have a bounded virtualized scroll region");
                var row = (ListBoxItem)cards.ItemContainerGenerator.ContainerFromIndex(1);
                row.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = Mouse.MouseWheelEvent
                });
                dialog.UpdateLayout();
                check(scroll.VerticalOffset > 0 && cards.SelectedIndex == 1,
                    "Extension cards scroll without losing selection");
                cards.ScrollIntoView(cards.Items[0]);
                cards.SelectedIndex = 0;
                dialog.UpdateLayout();
                CaptureProjectCards(dialog, Path.Combine(directory, "extension-cards.png"));
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
