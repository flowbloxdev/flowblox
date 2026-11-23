using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FlowBlox.UICore.PopUp.Views;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private static void CheckQuickStartImageZoom(
        ComponentPopupWindow popup,
        string directory,
        Action<bool, string> check)
    {
        var view = (ZoomableImageView)popup.FindName("StepImage");
        var picture = (Image)view.FindName("Picture");
        var scroller = (ScrollViewer)view.FindName("ImageScroller");
        popup.UpdateLayout();
        check(scroller.ScrollableWidth < 1 && scroller.ScrollableHeight < 1,
            "Quick Start images initially fit without scrollbars");
        var fittedWidth = picture.ActualWidth;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent
        };
        picture.RaiseEvent(wheel);
        popup.UpdateLayout();
        check(wheel.Handled && picture.ActualWidth > fittedWidth,
            "Mouse wheel zoom enlarges the Quick Start image without scrolling the page");

        var maximumWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 12000)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent
        };
        picture.RaiseEvent(maximumWheel);
        popup.UpdateLayout();
        check(Math.Abs(picture.Width - view.Source.Width * 4) < 1,
            "Quick Start image zoom is limited to 400 percent of its native size");
        var scrollbars = MenuVisuals<ScrollBar>(scroller).Where(bar => bar.IsVisible).ToList();
        check(scroller.ScrollableWidth > 0 && scroller.ScrollableHeight > 0 && scrollbars.Count == 2 &&
            scrollbars.All(bar => (bar.Orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight) <= 10),
            "Zoomed images expose slim horizontal and vertical MahApps scrollbars");
        scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth / 2);
        scroller.ScrollToVerticalOffset(scroller.ScrollableHeight / 2);
        popup.UpdateLayout();
        check(scroller.HorizontalOffset > 0 && scroller.VerticalOffset > 0,
            "A zoomed Quick Start image can be scrolled along both axes");

        foreach (var button in new[] { MouseButton.Left, MouseButton.Middle })
        {
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent
            };
            picture.RaiseEvent(press);
            check(press.Handled && scroller.IsMouseCaptured,
                $"The {button} mouse button captures the zoomed image for drag panning");
            picture.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
            {
                RoutedEvent = Mouse.PreviewMouseUpEvent
            });
            check(!scroller.IsMouseCaptured, $"Releasing {button} ends image panning");
        }

        SaveMenuImage(directory, (FrameworkElement)popup.Content, "quick-start-image-zoomed.png");
        var reset = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picture), Environment.TickCount, Key.Home)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        picture.RaiseEvent(reset);
        popup.UpdateLayout();
        check(reset.Handled && Math.Abs(picture.ActualWidth - fittedWidth) < 1 &&
            scroller.ScrollableWidth < 1 && scroller.ScrollableHeight < 1,
            "Home restores the fitted image and hides unnecessary scrollbars");

        picture.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 12000)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent
        });
        ((Button)popup.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        popup.UpdateLayout();
        check(scroller.ScrollableWidth < 1 && scroller.ScrollableHeight < 1,
            "Changing Quick Start steps resets zoom and scrolling for the next image");
        ((Button)popup.FindName("PreviousButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SaveMenuImage(directory, (FrameworkElement)popup.Content, "quick-start-image-fitted.png");
    }
}
