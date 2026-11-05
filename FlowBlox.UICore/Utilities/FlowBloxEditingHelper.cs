using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using System.Diagnostics;
using System.IO;

namespace FlowBlox.UICore.Utilities
{
    public class FlowBloxEditingHelper
    {
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
