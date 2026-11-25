namespace FlowBlox.Core.Models.Project.Launcher;

/// <summary>
/// Browses one server-selected artifact directory without following links or accepting paths outside that directory.
/// </summary>
public sealed class ProjectArtifacts
{
    private readonly string _root;
    public string RootDirectory => _root;

    public ProjectArtifacts(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(Path.GetPathRoot(_root))
            || string.Equals(_root, Path.GetPathRoot(_root)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An artifact directory must not be a filesystem root.", nameof(directory));
    }

    public IReadOnlyList<ProjectArtifactEntry> List(string relativeDirectory = "", int offset = 0, int limit = 1000)
    {
        if (offset < 0 || limit is < 1 or > 1001)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var path = Resolve(relativeDirectory);
        if (!Directory.Exists(path))
            return Array.Empty<ProjectArtifactEntry>();
        return new DirectoryInfo(path).EnumerateFileSystemInfos()
            .Where(item => !item.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .OrderByDescending(item => item is DirectoryInfo)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(offset)
            .Take(limit)
            .Select(item => new ProjectArtifactEntry
            {
                Name = item.Name,
                RelativePath = Path.GetRelativePath(_root, item.FullName).Replace('\\', '/'),
                IsDirectory = item is DirectoryInfo,
                Size = item is FileInfo file ? file.Length : 0,
                ModifiedUtc = item.LastWriteTimeUtc
            }).ToList();
    }

    /// <summary>
    /// Lists a bounded number of descendant directories without following symbolic links or junctions.
    /// </summary>
    public IReadOnlyList<ProjectArtifactEntry> ListDirectories(int limit = 21)
    {
        if (limit is < 1 or > 1001)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var root = Resolve(string.Empty);
        if (!Directory.Exists(root))
            return Array.Empty<ProjectArtifactEntry>();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        return Directory.EnumerateDirectories(root, "*", options)
            .Take(limit)
            .Select(directory => Path.GetRelativePath(root, directory))
            .Select(relativePath => new ProjectArtifactEntry
            {
                Name = Path.GetFileName(relativePath),
                RelativePath = Path.GetRelativePath(root, Resolve(relativePath)).Replace('\\', '/'),
                IsDirectory = true
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public int Count()
    {
        return EnumerateFiles().Count;
    }

    public FileStream OpenRead(string relativeFile)
    {
        var path = Resolve(relativeFile);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public int Clear()
    {
        // Preflight the entire tree before deleting anything. Keep the project output directory itself.
        var files = EnumerateFiles();
        var root = Resolve(string.Empty);
        if (!Directory.Exists(root))
            return 0;
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length).ToList();
        foreach (var directory in directories)
            Resolve(Path.GetRelativePath(_root, directory));
        foreach (var file in files)
            File.Delete(file);
        foreach (var directory in directories)
            Directory.Delete(directory, recursive: false);
        return files.Count;
    }

    private List<string> EnumerateFiles()
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        var root = Resolve(string.Empty);
        if (!Directory.Exists(root))
            return result;
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                var path = Resolve(Path.GetRelativePath(_root, entry.FullName));
                if (entry is DirectoryInfo)
                    pending.Push(path);
                else
                    result.Add(path);
            }
        }
        return result;
    }

    private string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            throw new UnauthorizedAccessException("Absolute artifact paths are not allowed.");
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!string.Equals(path, _root, StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Artifact path is outside the project directory.");
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new UnauthorizedAccessException("Artifact paths must not traverse symbolic links or junctions.");
        }
        return path;
    }
}
