using FlowBlox.Core.Constants;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.State
{
    internal static class SkillProjectSessionStore
    {
        // Skill projects currently receive no extensions, so Save() persists an empty .fbdeps file.
        // TODO: If the AI Assistant gains extension acquisition or installation support, transfer the
        // main project's already loaded extensions into skill projects without loading them again,
        // and persist those transferred dependencies in the skill project's .fbdeps file.

        // Active editing state and persisted skill projects are isolated per assistant session.
        private static readonly ConcurrentDictionary<string, string> ActiveProjectFiles =
            new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, FlowBloxProject> LoadedProjectCache =
            new(StringComparer.Ordinal);

        public static FlowBloxProject GetCurrentProject(string sessionId) =>
            TryGetCurrentProject(sessionId, out var project)
                ? project
                : throw new InvalidOperationException("No active project is loaded.");

        public static bool TryGetCurrentProject(string sessionId, out FlowBloxProject project)
        {
            if (TryGetActiveProject(sessionId, out project))
                return true;

            project = FlowBloxProjectManager.Instance.ActiveProject;
            return project != null;
        }

        public static bool TryGetActiveProject(string sessionId, out FlowBloxProject project)
        {
            return LoadedProjectCache.TryGetValue(NormalizeSessionId(sessionId), out project!);
        }

        public static bool IsActive(string sessionId) => TryGetActiveProject(sessionId, out _);

        public static bool TryGetActiveProjectInfo(
            string sessionId,
            out FlowBloxProject project,
            out string projectFile)
        {
            var normalizedSessionId = NormalizeSessionId(sessionId);
            if (LoadedProjectCache.TryGetValue(normalizedSessionId, out project!) &&
                ActiveProjectFiles.TryGetValue(normalizedSessionId, out projectFile!))
            {
                return true;
            }

            project = null!;
            projectFile = string.Empty;
            return false;
        }

        public static bool ProjectExists(string sessionId, string projectName) =>
            File.Exists(GetProjectFile(sessionId, projectName));

        public static (FlowBloxProject Project, string ProjectFile, bool Created) CreateOrUpdateFile(
            string sessionId,
            string projectName,
            FlowBloxProject project,
            string? existingProjectName = null)
        {
            var normalizedSessionId = NormalizeSessionId(sessionId);
            EnsureMainProjectScope(normalizedSessionId);
            var sourceProjectName = string.IsNullOrWhiteSpace(existingProjectName)
                ? projectName
                : existingProjectName.Trim();
            var sourceProjectFile = GetProjectFile(normalizedSessionId, sourceProjectName);
            var projectFile = GetProjectFile(normalizedSessionId, projectName);
            var sourceExists = File.Exists(sourceProjectFile);
            if (!string.IsNullOrWhiteSpace(existingProjectName) && !sourceExists)
            {
                throw new InvalidOperationException(
                    $"Skill project '{sourceProjectName}' does not exist in the current session.");
            }

            var isRename = sourceExists &&
                           !sourceProjectFile.Equals(projectFile, StringComparison.OrdinalIgnoreCase);
            if (isRename && File.Exists(projectFile))
            {
                throw new InvalidOperationException(
                    $"Cannot rename skill project to '{projectName}': a project file named " +
                    $"'{Path.GetFileName(projectFile)}' already exists in the current session.");
            }

            var created = !sourceExists;
            if (created)
            {
                project.ProjectGuid = Guid.NewGuid();
            }
            else
            {
                var projectGuidValue = JObject.Parse(File.ReadAllText(sourceProjectFile))
                    .Value<string>(nameof(FlowBloxProject.ProjectGuid));
                if (Guid.TryParse(projectGuidValue, out var projectGuid))
                    project.ProjectGuid = projectGuid;
            }

            project.ProjectName = projectName;
            if (string.IsNullOrWhiteSpace(project.ProjectDescription))
                throw new InvalidOperationException("The skill project description must not be empty.");

            FlowBloxProjectManager.Instance.InitializeScopedProject(project);
            try
            {
                project.Save(projectFile);
                if (isRename)
                    DeleteProjectFiles(sourceProjectFile);
                return (project, projectFile, created);
            }
            finally
            {
                FlowBloxProjectManager.Instance.CloseScopedProject(project);
            }
        }

        private static void DeleteProjectFiles(string projectFile)
        {
            foreach (var file in new[]
                     {
                         projectFile,
                         Path.ChangeExtension(projectFile, ".fbdeps"),
                         Path.ChangeExtension(projectFile, ".fblocaldata")
                     })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }

        public static FlowBloxProject Edit(string sessionId, string projectName)
        {
            var normalizedSessionId = NormalizeSessionId(sessionId);
            EnsureMainProjectScope(normalizedSessionId);
            var projectFile = GetProjectFile(normalizedSessionId, projectName);
            if (!File.Exists(projectFile))
                throw new InvalidOperationException($"Skill project '{projectName}' does not exist.");

            var project = SkillProjectSerializer.FromFile(projectFile);

            FlowBloxProjectManager.Instance.InitializeScopedProject(project);
            ActiveProjectFiles[normalizedSessionId] = projectFile;
            LoadedProjectCache[normalizedSessionId] = project;
            return project;
        }

        public static (FlowBloxProject Project, string ProjectFile) Save(string sessionId, string projectName)
        {
            var normalizedSessionId = NormalizeSessionId(sessionId);
            if (!LoadedProjectCache.TryGetValue(normalizedSessionId, out var project) ||
                !string.Equals(project.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Skill project '{projectName}' is not currently open. Open it before saving changes.");
            }
            if (string.IsNullOrWhiteSpace(project.ProjectDescription))
                throw new InvalidOperationException("The skill project description must not be empty.");

            var projectFile = ActiveProjectFiles.TryGetValue(normalizedSessionId, out var activeProjectFile)
                ? activeProjectFile
                : GetProjectFile(normalizedSessionId, projectName);
            EnsureSessionProjectPath(normalizedSessionId, projectFile);
            project.Save(projectFile);
            return (project, projectFile);
        }

        public static void Close(string sessionId)
        {
            var normalizedSessionId = NormalizeSessionId(sessionId);
            if (!LoadedProjectCache.TryRemove(normalizedSessionId, out var project))
                throw new InvalidOperationException("No skill project is currently open.");

            FlowBloxProjectManager.Instance.CloseScopedProject(project);
            ActiveProjectFiles.TryRemove(normalizedSessionId, out _);
        }

        public static IReadOnlyCollection<string> GetNames(string sessionId) =>
            GetProjects(sessionId)
                .Select(x => x.ProjectName)
                .ToArray();

        public static IReadOnlyCollection<(string ProjectName, string ProjectFile)> GetProjects(string sessionId) =>
            Directory.EnumerateFiles(GetSessionDirectory(sessionId), "*.fbprj", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Select(x => (ProjectName: ReadProjectName(x), ProjectFile: x))
                .OrderBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static string GetStoredProjectFile(string sessionId, string projectName) =>
            GetProjectFile(sessionId, projectName);

        public static void Clear(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            var normalizedSessionId = NormalizeSessionId(sessionId);
            ActiveProjectFiles.TryRemove(normalizedSessionId, out _);
            if (!LoadedProjectCache.TryRemove(normalizedSessionId, out var project))
                return;

            try
            {
                FlowBloxProjectManager.Instance.CloseScopedProject(project);
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    $"Failed to close the active skill project while clearing AI Assistant session '{normalizedSessionId}'.",
                    ex);
            }
        }

        private static string NormalizeSessionId(string? sessionId) =>
            string.IsNullOrWhiteSpace(sessionId) ? "default-session" : sessionId.Trim();

        private static string GetProjectFile(string sessionId, string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new InvalidOperationException("projectName does not contain a valid file name.");
            var projectFile = Path.Combine(
                GetSessionDirectory(sessionId),
                FlowBloxProjectFileNameHelper.FromProjectName(projectName));
            EnsureSessionProjectPath(sessionId, projectFile);
            return projectFile;
        }

        private static string GetSessionDirectory(string sessionId)
        {
            var localAppDataDirectory = FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.PathsLocalAppDataDir)?
                .Value;
            if (string.IsNullOrWhiteSpace(localAppDataDirectory))
            {
                localAppDataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlowBlox");
            }

            var normalizedSessionId = NormalizeSessionId(sessionId);
            var sessionHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSessionId)))
                .Substring(0, 16)
                .ToLowerInvariant();
            var directory = Path.Combine(
                localAppDataDirectory,
                "ai_assistant_skill_projects",
                sessionHash);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void EnsureSessionProjectPath(string sessionId, string projectFile)
        {
            var sessionDirectory = Path.GetFullPath(GetSessionDirectory(sessionId));
            var resolvedProjectFile = Path.GetFullPath(projectFile);
            var directoryWithSeparator = Path.TrimEndingDirectorySeparator(sessionDirectory) + Path.DirectorySeparatorChar;
            if (!resolvedProjectFile.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Skill projects may only be saved inside the current session directory.");
            }
        }

        private static string ReadProjectName(string projectFile)
        {
            try
            {
                return JObject.Parse(File.ReadAllText(projectFile)).Value<string>("ProjectName")?.Trim()
                       ?? Path.GetFileNameWithoutExtension(projectFile);
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    $"Failed to read the project name from skill project file '{projectFile}'.",
                    ex);
                return Path.GetFileNameWithoutExtension(projectFile);
            }
        }

        private static void EnsureMainProjectScope(string sessionId)
        {
            if (LoadedProjectCache.ContainsKey(sessionId))
            {
                throw new InvalidOperationException(
                    "Close the currently open skill project before opening or creating another one.");
            }
        }
    }
}
