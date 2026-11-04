using System.Diagnostics;
using System.IO;
using System.Windows;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Util.Resources;
using MahApps.Metro.Controls;

namespace FlowBlox.UICore.Views
{
    public partial class ExternalProjectExecutionConfirmationWindow : MetroWindow
    {
        private readonly string _projectFile;
        public bool RememberProjectApproval { get; private set; }

        public ExternalProjectExecutionConfirmationWindow(
            string projectFile,
            string reason,
            IReadOnlyDictionary<string, string>? inputParameters = null)
        {
            InitializeComponent();
            _projectFile = projectFile ?? string.Empty;

            ReasonText.Text = reason;
            InputParametersGrid.ItemsSource = inputParameters?
                .OrderBy(parameter => parameter.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ProjectFileText.Text = string.IsNullOrWhiteSpace(_projectFile)
                ? Text("Message_ExternalProjectExecution_ActiveSkillProject")
                : string.Format(Text("Message_ExternalProjectExecution_ProjectFile"), _projectFile);
            ReviewButton.IsEnabled = File.Exists(_projectFile) && File.Exists(ResolveFlowBloxExecutable());
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(Resources.AiAssistantChatView));

        private void ViewParameterContent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not KeyValuePair<string, string> parameter)
                return;

            var view = new EditContentView(parameter.Value, isReadOnly: true)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = parameter.Key
            };
            view.ShowDialog();
        }

        private void ReviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_projectFile))
                return;

            var executable = ResolveFlowBloxExecutable();
            if (!File.Exists(executable))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--project=\"{_projectFile.Replace("\"", string.Empty)}\"",
                UseShellExecute = true
            });
        }

        private static string ResolveFlowBloxExecutable() =>
            Path.Combine(GlobalPaths.CurrentDirectory, "FlowBlox.exe");

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            RememberProjectApproval = false;
            DialogResult = true;
            Close();
        }

        private void AlwaysAllowButton_Click(object sender, RoutedEventArgs e)
        {
            RememberProjectApproval = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
