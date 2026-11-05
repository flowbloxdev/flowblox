using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util.Resources;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlowBlox.AvaloniaDesktop.Interop.Wpf
{
    public static class WpfDialogHost
    {
        public static bool? ShowDialog(Window wpfDialog, Avalonia.Controls.Window owner)
        {
            var helper = new WindowInteropHelper(wpfDialog);
            helper.Owner = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

            wpfDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            void CenterOnOwner(object sender, RoutedEventArgs args)
            {
                wpfDialog.Loaded -= CenterOnOwner;
                CenterOnNativeOwner(helper.Handle, helper.Owner, owner);
            }

            wpfDialog.Loaded += CenterOnOwner;
            try
            {
                return InvokeShowDialog(() => wpfDialog.ShowDialog(), owner);
            }
            finally
            {
                wpfDialog.Loaded -= CenterOnOwner;
            }
        }

        private static void CenterOnNativeOwner(IntPtr dialogHandle, IntPtr ownerHandle, Avalonia.Controls.Window owner)
        {
            if (!GetWindowRect(ownerHandle, out var ownerBounds) || !GetWindowRect(dialogHandle, out var dialogBounds))
                return;

            // Avalonia and WPF can use different coordinate scales. Keep the complete
            // calculation in native screen pixels, including the monitor work area.
            var width = dialogBounds.Right - dialogBounds.Left;
            var height = dialogBounds.Bottom - dialogBounds.Top;
            var x = ownerBounds.Left + (ownerBounds.Right - ownerBounds.Left - width) / 2;
            var y = ownerBounds.Top + (ownerBounds.Bottom - ownerBounds.Top - height) / 2;
            var screen = owner.Screens.ScreenFromWindow(owner);
            if (screen != null)
            {
                var workArea = screen.WorkingArea;
                x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width));
                y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
            }

            const uint noSizeOrZOrderOrActivation = 0x0001 | 0x0004 | 0x0010;
            SetWindowPos(dialogHandle, IntPtr.Zero, x, y, 0, 0, noSizeOrZOrderOrActivation);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        private static TResult InvokeShowDialog<TResult>(Func<TResult> func, Avalonia.Controls.Window owner)
        {
            try
            {
                return func.Invoke();
            }
            catch(Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);

                DesktopDialogs.Messages.Show(FlowBloxResourceUtil.GetLocalizedString("WindowsFormWPFHelper_ShowDialog_Failure_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("WindowsFormWPFHelper_ShowDialog_Failure_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Error
                );
            }

            return default;
        }
    }
}
