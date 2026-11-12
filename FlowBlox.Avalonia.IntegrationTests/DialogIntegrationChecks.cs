using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlowBlox.AvaloniaDesktop.Dialogs;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.AvaloniaDesktop.Interop.Windows;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private static class DialogNative
    {
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }

    private async Task CheckDesktopDialogsAsync(Action<bool, string> check, string directory)
    {
        check(!_window.GetType().Assembly.GetReferencedAssemblies().Any(assembly =>
                assembly.Name is "System.Windows.Forms" or "WindowsFormsIntegration"),
            "Main application assembly has no WinForms or WindowsFormsIntegration references");
        check(HostedKeyboard.KeyFromVirtualKey(0x43) == global::Avalonia.Input.Key.C &&
              HostedKeyboard.KeyFromVirtualKey(0x25) == global::Avalonia.Input.Key.Left &&
              HostedKeyboard.KeyFromVirtualKey(0xA2) == global::Avalonia.Input.Key.LeftCtrl &&
              HostedKeyboard.KeyFromVirtualKey(0xA0) == global::Avalonia.Input.Key.LeftShift,
            "Hosted keyboard boundary translates command keys to Avalonia key types");
        var service = FlowBloxServiceLocator.Instance.GetService<IMessageBoxService>();
        var result = service.ShowAsync("Wollen Sie das Projekt wirklich schließen?", "Projekt schließen?",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        var box = _window.OwnedWindows.OfType<MessageBoxWindow>().Single();
        await Task.Delay(150);
        check(box.Owner == _window && box.Topmost && !box.ShowInTaskbar,
            "AXAML message box is owned by AppWindow and remains on top");
        check(!DialogNative.IsWindowEnabled(_window.Handle), "Message box disables AppWindow until answered");
        var ownerCenterX = _window.Position.X + _window.Bounds.Width * _window.RenderScaling / 2;
        var ownerCenterY = _window.Position.Y + _window.Bounds.Height * _window.RenderScaling / 2;
        check(Math.Abs(box.Position.X + box.Bounds.Width * box.RenderScaling / 2 - ownerCenterX) < 40 &&
              Math.Abs(box.Position.Y + box.Bounds.Height * box.RenderScaling / 2 - ownerCenterY) < 40,
            "AXAML message box is centered on AppWindow");
        using (var image = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize((int)box.Bounds.Width, (int)box.Bounds.Height)))
        {
            image.Render(box);
            image.Save(Path.Combine(directory, "MessageBox.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        box.FindControl<Button>("Negative").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(await result == MessageBoxResult.No && DialogNative.IsWindowEnabled(_window.Handle),
            "Message box returns No and restores AppWindow input");

        // Exercise the synchronous UICore contract while a real WPF modal window
        // pumps its dispatcher. Closing the message box must not enable AppWindow.
        var wpf = new FlowBlox.UICore.Views.EditValueWindow("TextBuilderAppend_1", false, false)
        {
            Title = "Create Flow Block ownership integration check"
        };
        Exception failure = null;
        wpf.ContentRendered += (_, _) =>
        {
            try
            {
                var wpfHandle = new System.Windows.Interop.WindowInteropHelper(wpf).Handle;
                var dialogCenter = wpf.PointToScreen(new System.Windows.Point(wpf.ActualWidth / 2, wpf.ActualHeight / 2));
                check(Math.Abs(dialogCenter.X - ownerCenterX) < 40 && Math.Abs(dialogCenter.Y - ownerCenterY) < 40,
                    "Create Flow Block naming dialog is centered on the Avalonia owner in screen coordinates");
                check(!DialogNative.IsWindowEnabled(_window.Handle), "WPF modal dialog disables AppWindow");
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var nested = _window.OwnedWindows.OfType<MessageBoxWindow>().Single();
                        check(nested.Topmost && !DialogNative.IsWindowEnabled(wpfHandle),
                            "Message box stays above WPF and blocks input to the WPF dialog");
                        nested.FindControl<Button>("Affirmative").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception ex) { failure = ex; _window.OwnedWindows.OfType<MessageBoxWindow>().FirstOrDefault()?.Close(); }
                });
                var legacy = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
                check(legacy.ShowMessageBox("Nachricht aus einem WPF-Dialog", "FlowBlox", FlowBloxMessageBoxTypes.Information)
                      == FlowBloxMessageBoxDialogResult.OK, "Synchronous UICore contract displays the AXAML message box");
                check(DialogNative.IsWindowEnabled(wpfHandle) && !DialogNative.IsWindowEnabled(_window.Handle),
                    "Closing the message box restores WPF input and preserves WPF modality");
            }
            catch (Exception ex) { failure = ex; }
            finally { wpf.Close(); }
        };
        WpfDialogHost.ShowDialog(wpf, _window);
        if (failure != null) throw failure;
        check(DialogNative.IsWindowEnabled(_window.Handle), "Closing WPF restores AppWindow input");

        // Open the actual Vault view with isolated sample data; merely selecting a card must not dirty it.
        var options = FlowBlox.Core.Util.FlowBloxOptions.GetOptionInstance();
        var originalVault = options.GetOption(FlowBlox.Core.Constants.OptionsConstants.VaultValues);
        try
        {
            var sampleVault = new FlowBlox.Core.Models.Components.OptionElement(
                FlowBlox.Core.Constants.OptionsConstants.VaultValues, "", "", FlowBlox.Core.Models.Components.OptionElement.OptionType.Password);
            sampleVault.PersistentValue = "[{\"Name\":\"JiraPassword\",\"Value\":\"sample-password\",\"IsPassword\":true}]";
            options.OptionCollection[FlowBlox.Core.Constants.OptionsConstants.VaultValues] = sampleVault;
            var vault = new FlowBlox.UICore.Views.VaultWindow();
            vault.ContentRendered += (_, _) =>
            {
                try
                {
                    var cards = (System.Windows.Controls.ListBox)vault.FindName("EntriesList");
                    var editor = (System.Windows.FrameworkElement)vault.FindName("EntryEditor");
                    var emptyHint = (System.Windows.FrameworkElement)vault.FindName("NoSelectionHint");
                    check(!vault.HasSelection && !editor.IsVisible && emptyHint.IsVisible,
                        "Vault shows a hint instead of the editor when no card is selected");
                    cards.SelectedIndex = 0;
                    vault.UpdateLayout();
                    check(vault.HasSelection && !vault.IsDirty, "Selecting a Vault card leaves the view unchanged");
                    check(editor.IsVisible && !emptyHint.IsVisible,
                        "Selecting a Vault card reveals the editor and hides the hint");
                    check(((System.Windows.Controls.TextBox)vault.FindName("ValueEditor")).Visibility == System.Windows.Visibility.Collapsed,
                        "Vault password card uses the masked editor");
                    cards.SelectedIndex = -1;
                    vault.UpdateLayout();
                    check(!editor.IsVisible && emptyHint.IsVisible && !vault.IsDirty,
                        "Clearing the Vault selection restores the hint without changing entries");
                }
                catch (Exception exception) { failure = exception; }
                finally { vault.Close(); }
            };
            WpfDialogHost.ShowDialog(vault, _window);
            if (failure != null) throw failure;
        }
        finally { options.OptionCollection[FlowBlox.Core.Constants.OptionsConstants.VaultValues] = originalVault; }

        // A modeless WPF window must still accept characters without ElementHost.
        var text = new System.Windows.Controls.TextBox();
        var modeless = new System.Windows.Window { Width = 300, Height = 150, Content = text };
        new System.Windows.Interop.WindowInteropHelper(modeless).Owner = _window.Handle;
        try
        {
            modeless.Show();
            text.Focus();
            await Task.Delay(100);
            DialogNative.SendMessage(new System.Windows.Interop.WindowInteropHelper(modeless).Handle,
                0x0102, new IntPtr('x'), IntPtr.Zero);
            check(text.Text.Contains('x'), "Modeless WPF window receives keyboard input without WinForms interop");
        }
        finally { modeless.Close(); }
    }
}
