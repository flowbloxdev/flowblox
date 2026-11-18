using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectOutputDirectoryChoice
{
    public string ProjectName { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string DisplayName => ProjectName;

    public static IReadOnlyList<ProjectOutputDirectoryChoice> Create(IEnumerable<string> projectNames)
    {
        return projectNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new ProjectOutputDirectoryChoice
            {
                ProjectName = name,
                Value = Path.Combine("$Options::" + OptionsConstants.PathsOutputDir, IOUtil.GetValidFileName(name).Trim())
            })
            .ToList();
    }
}
