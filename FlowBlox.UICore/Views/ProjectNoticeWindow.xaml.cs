using System.Diagnostics;
using System.Windows;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Projects;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using MahApps.Metro.Controls;
using Texts = FlowBlox.UICore.Resources.ProjectNoticeWindow;

namespace FlowBlox.UICore.Views;

/// <summary>
/// Presents project guidance with the existing Markdown renderer and explicit acknowledgement.
/// </summary>
public partial class ProjectNoticeWindow : MetroWindow
{
    private readonly FlowBloxProject _project;

    public ProjectNoticeWindow(FlowBloxProject project)
    {
        InitializeComponent();
        _project = project;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        ProjectDescription.Text = string.Format(
            FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.Description), typeof(Texts)),
            project.ProjectName);
        NoticeMarkdown.Markdown = project.Notice ?? string.Empty;
        NoticeMarkdown.Engine.HyperlinkCommand = new RelayCommand(OpenLink);
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (ReadNotice.IsChecked == true)
        {
            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(OptionsConstants.ProjectsReadNotices);
            option.PersistentValue = ProjectNoticeReadState.MarkRead(_project, option.PersistentValue);
            options.Save();
        }

        DialogResult = true;
    }

    private static void OpenLink(object parameter)
    {
        if (!Uri.TryCreate(parameter?.ToString(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Warn("Failed to open a project notice hyperlink.", exception);
        }
    }
}
