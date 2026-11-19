using System.Diagnostics;
using Newtonsoft.Json;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectExecutions
{
    public static ProjectExecutions Instance { get; } = new(
        Path.Combine(Path.GetDirectoryName(GlobalPaths.RecentProjectsPath)!, "fbexecutions.json"));

    private readonly LauncherJsonStore<ProjectExecutionEntry> _store;
    private readonly string _directory;
    private readonly object _refreshLock = new();

    public event Action<ProjectExecutionEntry>? ExecutionCompleted;

    public ProjectExecutions(string path)
    {
        _store = new LauncherJsonStore<ProjectExecutionEntry>(path);
        _directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "executions");
    }

    public IReadOnlyList<ProjectExecutionEntry> GetAll() => _store.Read()
        .OrderByDescending(x => x.StartedUtc).ToList();

    public static Dictionary<string, List<ProjectOutputDatasetDto>> ReadOutputs(ProjectExecutionEntry entry)
    {
        // The original response also preserves values from older history entries serialized as JsonElement metadata.
        if (File.Exists(entry.ResponseFile))
            return RunnerJson.ReadFile<RunnerResponse>(entry.ResponseFile)?.Outputs ?? new();
        return entry.Response?.Outputs ?? new();
    }

    public IReadOnlyList<ProjectExecutionEntry> GetForProject(ProjectShortcut project)
    {
        var identity = project;
        if (string.IsNullOrWhiteSpace(project.ProjectFile) && string.IsNullOrWhiteSpace(project.ApiEndpoint))
        {
            identity = new ProjectShortcut
            {
                ProjectSpaceGuid = project.ProjectSpaceGuid,
                ApiEndpoint = ProjectShortcut.CurrentApiEndpoint
            };
        }

        return GetAll()
            .Where(entry => ProjectShortcutIdentity.Matches(entry.Project, identity))
            .ToList();
    }

    public ProjectExecutionEntry Start(
        ProjectShortcut shortcut,
        FlowBloxProject project,
        RunnerRequest request,
        string inputDirectory,
        string outputDirectory)
    {
        using var operation = InterprocessFileLock.Acquire(Path.Combine(_directory, "operations"));
        var id = Guid.NewGuid();
        var directory = Path.Combine(_directory, id.ToString("N"));
        Directory.CreateDirectory(directory);

        // Online projects are loaded from their saved endpoint, then passed to the runner as an isolated snapshot.
        if (string.IsNullOrWhiteSpace(shortcut.ProjectFile))
        {
            request.ProjectFile = Path.Combine(directory, "project.fbprj");
            SaveRunnerSnapshot(project, request.ProjectFile);
        }
        else
        {
            request.ProjectFile = shortcut.ProjectFile;
        }
        request.ProjectSpaceGuid = null;
        request.PasswordFieldNames = (project.UserFields ?? Enumerable.Empty<FieldElement>())
            .Where(field => field.IsUserInputField() && field.IsPassword)
            .Select(field => field.Name)
            .ToList();

        var entry = new ProjectExecutionEntry
        {
            Id = id,
            Project = new ProjectShortcut
            {
                Name = shortcut.Name,
                ProjectFile = shortcut.ProjectFile,
                ProjectSpaceGuid = shortcut.ProjectSpaceGuid,
                ApiEndpoint = string.IsNullOrWhiteSpace(shortcut.ApiEndpoint)
                    ? ProjectShortcut.CurrentApiEndpoint
                    : shortcut.ApiEndpoint
            },
            Request = request,
            ProjectDescription = project.ProjectDescription ?? string.Empty,
            StartedUtc = DateTime.UtcNow,
            InputDirectory = inputDirectory,
            OutputDirectory = outputDirectory,
            RequestFile = Path.Combine(directory, "request.json"),
            ResponseFile = Path.Combine(directory, "response.json")
        };
        RunnerJson.WriteProtectedRequestFile(entry.RequestFile, request);
        _store.Update(entries => entries.Add(entry));

        try
        {
            var command = RunnerHostResolver.Resolve();
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.BuildArguments(entry.RequestFile, entry.ResponseFile),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            }) ?? throw new InvalidOperationException("RunnerHost did not start.");
            entry.ProcessId = process.Id;
            entry.ProcessStartedUtc = process.StartTime.ToUniversalTime();
            _store.Update(entries =>
            {
                var storedEntry = entries.Single(x => x.Id == entry.Id);
                storedEntry.ProcessId = entry.ProcessId;
                storedEntry.ProcessStartedUtc = entry.ProcessStartedUtc;
            });
        }
        catch (Exception exception)
        {
            entry.LaunchError = exception.Message;
            _store.Update(entries =>
            {
                var storedEntry = entries.Single(x => x.Id == entry.Id);
                storedEntry.LaunchError = exception.Message;
            });
            throw;
        }
        return entry;
    }

    internal static void SaveRunnerSnapshot(FlowBloxProject project, string path)
    {
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        // Dashboard imports are data models until the manager initializes their registry.
        // Save serializes the registry, so initialize before creating the isolated runner copy.
        if (!project.FlowBloxRegistry.GetFlowBlocks().Any())
            FlowBloxProjectManager.Instance.InitializeScopedProject(project);

        project.Save(path);
    }

    public bool Refresh()
    {
        lock (_refreshLock)
            return RefreshCore();
    }

    public bool CanTerminate(ProjectExecutionEntry entry)
    {
        using var process = GetActiveProcess(entry);
        return process != null;
    }

    private static Process? GetActiveProcess(ProjectExecutionEntry entry)
    {
        if (!entry.IsRunning || entry.ProcessId is not > 0 || !entry.ProcessStartedUtc.HasValue)
            return null;

        Process? process = null;
        try
        {
            process = Process.GetProcessById(entry.ProcessId.Value);
            // Windows can reuse a PID. Only the original runner instance may be terminated.
            if (!process.HasExited && process.StartTime.ToUniversalTime() == entry.ProcessStartedUtc.Value)
                return process;
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }

        process?.Dispose();
        return null;
    }

    public bool Terminate(Guid executionId)
    {
        lock (_refreshLock)
        {
            RefreshCore();
            var entry = _store.Read().SingleOrDefault(candidate => candidate.Id == executionId);
            if (entry == null)
                return false;

            using var process = GetActiveProcess(entry);
            if (process == null)
                return false;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                RefreshCore();
                return false;
            }
            if (!process.WaitForExit(5000))
                throw new TimeoutException("The runner process did not exit after termination.");

            // Preserve a response that the runner finished writing just before it exited.
            RefreshCore();
            if (_store.Read().Single(candidate => candidate.Id == executionId).IsRunning)
            {
                RunnerJson.WriteFile(entry.ResponseFile, new RunnerResponse
                {
                    Success = false,
                    ExitCode = RunnerExitCodes.Aborted,
                    ProjectName = entry.Project.Name,
                    StartedUtc = entry.StartedUtc,
                    FinishedUtc = DateTime.UtcNow,
                    CancellationKind = RuntimeCancellationKind.UserCancellation,
                    CancellationReason = FlowBloxResourceUtil.GetLocalizedString(
                        "ProjectExecutions_ProcessTerminated", typeof(FlowBloxTexts))
                });
                RefreshCore();
            }
            return true;
        }
    }

    private bool RefreshCore()
    {
        var completed = new Dictionary<Guid, RunnerResponse>();
        foreach (var entry in _store.Read().Where(x => x.IsRunning && File.Exists(x.ResponseFile)))
        {
            try
            {
                var response = JsonConvert.DeserializeObject<RunnerResponse>(
                    File.ReadAllText(entry.ResponseFile),
                    new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                if (response != null && response.FinishedUtc != default)
                    completed[entry.Id] = response;
            }
            catch (IOException)
            {
                // The runner may still be writing the response; retry on the next refresh.
            }
            catch (JsonException)
            {
                // An incomplete response does not complete the execution.
            }
        }
        if (completed.Count == 0)
            return false;

        _store.Update(entries =>
        {
            foreach (var entry in entries)
            {
                if (completed.TryGetValue(entry.Id, out var response))
                    entry.Response = response;
            }
        });
        foreach (var entry in _store.Read().Where(entry => completed.ContainsKey(entry.Id)))
            ExecutionCompleted?.Invoke(entry);

        return true;
    }

    /// <summary>Resets output only while no active desktop or web runner uses the same directory tree.</summary>
    public int ClearArtifacts(string directory)
    {
        using var operation = InterprocessFileLock.Acquire(Path.Combine(_directory, "operations"));
        Refresh();
        if (IsArtifactDirectoryInUse(directory))
            throw new InvalidOperationException("An active execution is using this artifact directory.");
        return new ProjectArtifacts(directory).Clear();
    }

    public bool IsArtifactDirectoryInUse(string directory)
    {
        var output = Path.GetFullPath(directory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return GetAll().Where(CanTerminate).Any(entry =>
        {
            var activeOutput = Path.GetFullPath(entry.OutputDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return output.StartsWith(activeOutput, StringComparison.OrdinalIgnoreCase)
                || activeOutput.StartsWith(output, StringComparison.OrdinalIgnoreCase);
        });
    }

    // Removing a shortcut never terminates its runner or deletes its request/response/log files.
    public void Remove(Guid id) => _store.Update(entries => entries.RemoveAll(x => x.Id == id));
}
