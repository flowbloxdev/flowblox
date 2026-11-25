using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckProjectNoticeMarkdown(Action<bool, string> check, string directory)
    {
        foreach (var markdown in new[] { true, false })
        {
            var project = new FlowBloxProject
            {
                ProjectName = "Notice rendering",
                Notice = markdown
                    ? "## Setup\n\nRead the [setup guide](https://example.com/guide).\n\n- First input\n- Second input"
                    : "Plain project notes\n\nKeep this line readable."
            };
            var shortcut = new ProjectShortcut { Name = project.ProjectName, ProjectFile = Path.Combine(directory, "notice.fbprj") };
            var dialog = new ProjectExecutionWindow(shortcut, project);
            ((Expander)dialog.FindName("ProjectNoticeExpander")).IsExpanded = true;
            Exception failure = null;
            dialog.ContentRendered += (_, _) =>
            {
                try
                {
                    var viewer = FindLauncherVisuals<FlowDocumentScrollViewer>(dialog).Single();
                    var document = viewer.Document;
                    var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
                    if (markdown)
                    {
                        check(document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.FontSize > document.FontSize
                                && new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim() == "Setup"),
                            "Run Project renders Markdown section headings");
                        check(document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines.OfType<Hyperlink>())
                                .Any(link => (link.NavigateUri?.AbsoluteUri ?? link.CommandParameter?.ToString()) == "https://example.com/guide"),
                            "Run Project renders labeled Markdown hyperlinks");
                        check(document.Blocks.OfType<System.Windows.Documents.List>().Any(),
                            "Run Project renders Markdown lists");
                    }
                    else
                    {
                        check(text.Contains("Plain project notes", StringComparison.Ordinal)
                                && text.Contains("Keep this line readable.", StringComparison.Ordinal),
                            "Run Project keeps unformatted project notes readable");
                    }
                    CaptureProjectCards(dialog, Path.Combine(directory, markdown ? "run-project-markdown.png" : "run-project-plain-text.png"));
                }
                catch (Exception exception) { failure = exception; }
                finally { dialog.Close(); }
            };
            WpfDialogHost.ShowDialog(dialog, _window);
            if (failure != null)
                throw failure;
        }
    }
}
