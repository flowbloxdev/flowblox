using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlowBlox.AvaloniaDesktop.Dialogs;
using FlowBlox.UICore.Views;
using Border = System.Windows.Controls.Border;
using Button = Avalonia.Controls.Button;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckConnectionPreviewBeforeMessageAsync(Action<bool, string> check)
    {
        _window.ActivatePanel(ProjectPanelView);
        await Task.Delay(150);
        var control = ProjectPanelView.WpfControl;
        var model = control.ViewModel;
        var canvas = (FrameworkElement)control.FindName("ProjectCanvas");
        var previewNames = new[] { "ConnectionPreviewLine", "ConnectionPreviewStartDot", "ConnectionPreviewArrowHead" };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var begin = typeof(ProjectPanelWpfControl).GetMethod("FlowBlock_PreviewMouseLeftButtonDown", flags);
        var finish = typeof(ProjectPanelWpfControl).GetMethod("FinishConnection", flags);
        var activeGesture = typeof(ProjectPanelWpfControl).GetField("_isConnectingNodes", flags);
        var node = model.Nodes.Last(model.CanStartConnectionFrom);
        var endPoint = new Point(node.X + node.Width / 2d, node.Y + node.Height / 2d);
        check(ReferenceEquals(model.GetNodeAt(endPoint), node), "Connection rejection uses an unambiguous self-connection target");

        foreach (var temporary in new[] { false, true })
        {
            var mode = temporary ? "Ctrl+Shift" : "toolbar";
            Exception failure = null;
            var observedDialog = false;
            try
            {
                model.SelectModeCommand.Execute(null);
                if (temporary)
                    model.SetTemporaryConnectionMode(true);
                else
                    model.ConnectionModeCommand.Execute(null);

                begin.Invoke(control, new object[]
                {
                    new Border { DataContext = node },
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    {
                        RoutedEvent = Mouse.PreviewMouseDownEvent
                    }
                });
                check((bool)activeGesture.GetValue(control) && ReferenceEquals(Mouse.Captured, canvas),
                    $"{mode} connection gesture starts with canvas capture");

                Dispatcher.UIThread.Post(() =>
                {
                    var box = _window.OwnedWindows.OfType<MessageBoxWindow>().SingleOrDefault();
                    try
                    {
                        check(box != null, $"{mode} rejected connection opens the real message box");
                        observedDialog = true;
                        check(!(bool)activeGesture.GetValue(control) && !ReferenceEquals(Mouse.Captured, canvas),
                            $"{mode} connection gesture and capture end before the message box");
                        canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                        {
                            RoutedEvent = Mouse.MouseMoveEvent
                        });
                        check(previewNames.All(name => ((UIElement)control.FindName(name)).Visibility == Visibility.Collapsed),
                            $"{mode} preview stays hidden on mouse movement while the message box is open");
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        box?.FindControl<Button>("Affirmative").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                });

                finish.Invoke(control, new object[] { endPoint });
                if (failure != null)
                    throw failure;
                check(observedDialog && !(bool)activeGesture.GetValue(control),
                    $"{mode} rejected connection remains finished after closing the message box");
            }
            finally
            {
                control.DeactivateTemporaryConnectionShortcut();
                model.SelectModeCommand.Execute(null);
            }
        }
    }
}
