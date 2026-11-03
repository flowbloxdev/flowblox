using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util.Resources;
using System;
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
            return InvokeShowDialog(() => wpfDialog.ShowDialog(), owner);
        }

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
