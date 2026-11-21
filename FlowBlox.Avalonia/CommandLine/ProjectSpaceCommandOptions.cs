using CommandLine;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;

namespace FlowBlox.AvaloniaDesktop.CommandLine;

internal sealed class ProjectSpaceCommandOptions
{
    [Option("list-ps-projects", HelpText = "List public Project Space projects as JSON.")]
    public bool List { get; set; }

    [Option("download-ps-project", HelpText = "Download the latest project by Project Space GUID.")]
    public string Download { get; set; }

    [Option("reload-ps-project", HelpText = "Replace a local PS-bound project with current online content; requires --overwrite.")]
    public string Reload { get; set; }

    [Option("upload-ps-project", HelpText = "Upload a local .fbprj; update its binding or create a private project.")]
    public string Upload { get; set; }

    [Option("private", HelpText = "Use remembered credentials; list only your private projects.")]
    public bool Private { get; set; }

    [Option("mine", HelpText = "List all your projects, including public projects, using remembered credentials.")]
    public bool Mine { get; set; }

    [Option("query", HelpText = "Filter the project list by name.")]
    public string Query { get; set; }

    [Option("directory", HelpText = "Download directory; defaults to the configured project directory.")]
    public string Directory { get; set; }

    [Option("overwrite", HelpText = "Replace the local project and its sidecars during download or reload.")]
    public bool Overwrite { get; set; }

    [Option("new", HelpText = "Upload as a new project instead of updating the local binding.")]
    public bool New { get; set; }

    [Option("description", HelpText = "PS description when UseProjectDescriptionForProjectSpace=false.")]
    public string Description { get; set; }

    [Option("visibility", HelpText = "Upload visibility: private or public. Existing visibility is preserved by default.")]
    public string Visibility { get; set; }

    [Option("classification", HelpText = "Project classification: Solution or Function. Filters lists; sets upload metadata. Existing classification is otherwise preserved.")]
    public FbProjectClassification? Classification { get; set; }

    [Option("ps-help", HelpText = "Show Project Space command help without opening the UI.")]
    public bool Help { get; set; }
}
