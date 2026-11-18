namespace FlowBlox.Core.Models.Project.Launcher;

/// <summary>
/// Keeps local copies distinct and identifies remote projects by their service and GUID.
/// </summary>
public static class ProjectShortcutIdentity
{
    public static bool Matches(ProjectShortcut first, ProjectShortcut second)
    {
        if (!string.IsNullOrWhiteSpace(first.ProjectFile) || !string.IsNullOrWhiteSpace(second.ProjectFile))
        {
            return !string.IsNullOrWhiteSpace(first.ProjectFile)
                && !string.IsNullOrWhiteSpace(second.ProjectFile)
                && string.Equals(
                    Path.GetFullPath(first.ProjectFile),
                    Path.GetFullPath(second.ProjectFile),
                    StringComparison.OrdinalIgnoreCase);
        }

        return Guid.TryParse(first.ProjectSpaceGuid, out var firstGuid)
            && Guid.TryParse(second.ProjectSpaceGuid, out var secondGuid)
            && firstGuid == secondGuid
            && !string.IsNullOrWhiteSpace(first.ApiEndpoint)
            && !string.IsNullOrWhiteSpace(second.ApiEndpoint)
            && string.Equals(
                first.ApiEndpoint.Trim().TrimEnd('/'),
                second.ApiEndpoint.Trim().TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
    }
}
