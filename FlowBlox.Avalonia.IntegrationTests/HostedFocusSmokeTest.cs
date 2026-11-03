using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dock.Model.Controls;
using Dock.Model.Core;
using FlowBlox.AppWindow.Contents;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private static class FocusTestNative
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
        [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }

    private async Task CheckHostedFocusAsync(Action<bool, string> check)
    {
        _window.ActivatePanel(ProjectPanelView);
        _window.ActivatePanel(FieldPanel);
        _window.UpdateLayout();
        await Task.Delay(250);

        void FocusShell()
        {
            ((global::Avalonia.Controls.Control)MainMenu.Items[0]).Focus();
            FocusTestNative.SetFocus(_window.Handle);
            check(FocusTestNative.GetFocus() == _window.Handle, "Native focus moved to Avalonia shell");
        }

        void Click(WpfPanel panel, Point location)
        {
            var dpi = VisualTreeHelper.GetDpi(panel.WpfContent);
            var position = new IntPtr(((int)(location.Y * dpi.DpiScaleY) << 16) |
                                     ((int)(location.X * dpi.DpiScaleX) & 0xffff));
            FocusTestNative.SendMessage(panel.Handle, 0x0021, _window.Handle, new IntPtr(1 | (0x0201 << 16)));
            FocusTestNative.SendMessage(panel.Handle, 0x0200, IntPtr.Zero, position);
            FocusTestNative.SendMessage(panel.Handle, 0x0201, new IntPtr(1), position);
            FocusTestNative.SendMessage(panel.Handle, 0x0202, IntPtr.Zero, position);
        }

        FocusShell();
        Click(ProjectPanelView, new Point(30, 150));
        await Task.Delay(100);
        check(FocusTestNative.GetFocus() == ProjectPanelView.Handle &&
              ProjectPanelView.HasWpfKeyboardFocus,
            "Blank project canvas click receives native and WPF keyboard focus");
        var project = _window.WorkspaceFactory.GetDockable<IDockable>(nameof(ProjectPanel));
        check(((IDock)project.Owner).ActiveDockable == project &&
              ((IRootDock)Workspace.Layout).FocusedDockable == project,
            "Project canvas click synchronizes active and focused dock");

        var textBox = (TextBox)FieldPanel.WpfContent.FindName("FilterTextBox");
        var previousText = textBox.Text;
        FocusShell();
        // A standard WPF control takes focus itself. Verify that the host's
        // GotKeyboardFocus bridge preserves that target instead of focusing its root.
        textBox.Focus();
        await Task.Delay(100);
        check(ReferenceEquals(Keyboard.FocusedElement, textBox) &&
              FocusTestNative.GetFocus() == FieldPanel.Handle, "WPF text field retains its own keyboard focus");
        FocusTestNative.SendMessage(FieldPanel.Handle, 0x0102, new IntPtr('x'), IntPtr.Zero);
        check(textBox.Text.Length == previousText.Length + 1, "WPF text field still accepts keyboard input");
        textBox.Text = previousText;

        FocusShell();
        Click(ProjectPanelView, new Point(30, 150));
        await Task.Delay(100);
        check(ProjectPanelView.HasWpfKeyboardFocus && !textBox.IsKeyboardFocusWithin,
            "Project canvas regains focus after text field and Avalonia shell");
    }
}
