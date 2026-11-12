using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.ViewModels.PSProjects;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckProjectSpaceCards(Action<bool, string> check, string directory)
    {
        var dialog = new PSProjectsWindow(loadRemoteData: false);
        var model = (PSProjectsViewModel)dialog.DataContext;
        model.SearchResults = Enumerable.Range(1, 40).Select(index => new FbProjectResult
        {
            Guid = Guid.NewGuid().ToString(),
            Name = index == 1 ? "Duplicate File Review - Build a safe deletion script" : $"Project Space example {index}",
            Description = "Find duplicate files recursively, verify their binary content and prepare a deletion script for manual review.",
            UpdatedAt = new DateTime(2026, 11, 10, 12, 30, 0),
            Versions = [new FbProjectVersionResult { VersionNumber = index, Comment = "Published project" }]
        }).ToList();
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var cards = (ListBox)dialog.FindName("ProjectCards");
                cards.SelectedIndex = 0;
                model.SelectedVersion = model.ProjectVersions.Single();
                cards.SelectedIndex = 1;
                dialog.UpdateLayout();
                check(ReferenceEquals(model.SelectedProject, model.SearchResults[1]) && model.SelectedVersion == null,
                    "Project cards update the selected project and clear the previous version selection");
                check(model.ProjectVersions.Single().VersionNumber == 2 && model.OpenProjectCommand.CanExecute(null),
                    "Project cards retain version details and the open-project action");

                var scroll = FindFieldSelectionVisual<ScrollViewer>(cards);
                var row = (ListBoxItem)cards.ItemContainerGenerator.ContainerFromIndex(1);
                check(scroll != null && scroll.ScrollableHeight > 0, "Project cards have a bounded vertical scroll range");
                row.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = Mouse.MouseWheelEvent
                });
                dialog.UpdateLayout();
                check(scroll.VerticalOffset > 0 && cards.SelectedIndex == 1,
                    "Mouse wheel scrolls project cards without losing selection");

                cards.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                });
                check(dialog.IsVisible, "Double-clicking project-list chrome does not open the selected project");

                cards.ScrollIntoView(cards.Items[0]);
                cards.SelectedIndex = 0;
                dialog.UpdateLayout();
                CaptureProjectCards(dialog, Path.Combine(directory, "project-space-cards.png"));
                cards.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), Environment.TickCount, Key.Enter)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                });
            }
            catch (Exception exception)
            {
                failure = exception;
                dialog.Close();
            }
        };
        WpfDialogHost.ShowDialog(dialog, _window);
        if (failure != null)
            throw failure;
        var selection = dialog.Tag as PSProjectSelection;
        check(selection?.Project == model.SearchResults[0], "Enter opens the selected project card through the existing selection contract");
    }

    private static void CaptureProjectCards(Window dialog, string path)
    {
        var content = (FrameworkElement)dialog.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(Brushes.White, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
