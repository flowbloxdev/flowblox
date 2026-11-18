using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Views;
using System.Windows;
using System.Diagnostics;
using System.IO;

namespace FlowBlox.UICore.Utilities
{
    public class FlowBloxEditingHelper
    {
        public static void OpenFileWithSelection(string path, Window owner)
        {
            if (!File.Exists(path))
                return;

            var dialog = new MultiValueSelectionDialog(
                FlowBloxResourceUtil.GetLocalizedString("FileOpenMode_Dialog_Title", typeof(Core.FlowBloxTexts)),
                FlowBloxResourceUtil.GetLocalizedString("FileOpenMode_Dialog_Message", typeof(Core.FlowBloxTexts)),
                new GenericSelectionHandler<FileOpenMode>(
                    Enum.GetValues(typeof(FileOpenMode)).Cast<FileOpenMode>().ToList(),
                    mode => mode.GetDisplayName()))
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() != true)
                return;

            if (dialog.SelectedItem.Value is FileOpenMode.FlowBloxEditor)
                OpenUsingEditor(path);
            else
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public static void OpenUsingEditor(string value, string subject)
        {
            subject = IOUtil.GetValidFileName(subject);
            string path = Path.Combine(Path.GetTempPath(), "FlowBlox", $"{subject}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, value);
            OpenUsingEditor(path);
        }

        public static void OpenUsingEditor(string path)
        {
            if (File.Exists(path))
            {
                string editorPath = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.EditorDefaultPath].Value;
                if (File.Exists(editorPath))
                {
                    Process process = new Process();
                    process.StartInfo.FileName = editorPath;
                    process.StartInfo.Arguments = "\"" + path + "\"";
                    process.Start();
                }
                else
                {
                    FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>()
                        .ShowMessageBox(
                            string.Format(Resources.OptionsWindow.EditorPathError_Message, editorPath),
                            Resources.OptionsWindow.EditorPathError_Title,
                            FlowBloxMessageBoxTypes.Information);
                }
            }
        }
    }
}
