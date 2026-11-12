using FlowBlox.Core.Constants;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectSpaceFavorites
{
    public static ProjectSpaceFavorites Instance { get; } = new(
        Path.Combine(Path.GetDirectoryName(GlobalPaths.RecentProjectsPath)!, "psfavorites.json"));

    private readonly LauncherJsonStore<ProjectShortcut> _store;
    public event EventHandler? Changed;

    public ProjectSpaceFavorites(string path)
    {
        _store = new LauncherJsonStore<ProjectShortcut>(path);
    }

    public IReadOnlyList<ProjectShortcut> GetAll() => _store.Read();

    public bool Contains(string endpoint, string guid) => _store.Read()
        .Any(x => Matches(x, endpoint, guid));

    public void SetFavorite(string endpoint, string guid, string name, bool favorite)
    {
        if (!Guid.TryParse(guid, out var projectGuid))
            throw new ArgumentException("A Project Space GUID is required.", nameof(guid));
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("An HTTP(S) Project Space endpoint is required.", nameof(endpoint));

        _store.Update(entries =>
        {
            entries.RemoveAll(x => Matches(x, endpoint, guid));
            if (favorite)
            {
                entries.Add(new ProjectShortcut
                {
                    Name = name,
                    ProjectSpaceGuid = projectGuid.ToString(),
                    ApiEndpoint = endpoint.Trim().TrimEnd('/')
                });
            }
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool Matches(ProjectShortcut entry, string endpoint, string guid) =>
        string.Equals(entry.ApiEndpoint.TrimEnd('/'), endpoint.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(entry.ProjectSpaceGuid, out var storedGuid)
        && Guid.TryParse(guid, out var requestedGuid)
        && storedGuid == requestedGuid;
}
