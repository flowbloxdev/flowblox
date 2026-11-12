using System.Diagnostics;
using System.Text.Json;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectExecutions
{
    public static ProjectExecutions Instance { get; } = new(
        Path.Combine(Path.GetDirectoryName(GlobalPaths.RecentProjectsPath)!, "fbexecutions.json"));

    private readonly LauncherJsonStore<ProjectExecutionEntry> _store;
    private readonly string _directory;

    public ProjectExecutions(string path)
    {
        _store = new LauncherJsonStore<ProjectExecutionEntry>(path);
        _directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "executions");
    }

    public IReadOnlyList<ProjectExecutionEntry> GetAll() => _store.Read()
        .OrderByDescending(x => x.StartedUtc).ToList();

    public ProjectExecutionEntry Start(
        ProjectShortcut shortcut,
        FlowBloxProject project,
        RunnerRequest request,
        string inputDirectory,
        string outputDirectory)
    {
        var id = Guid.NewGuid();
        var directory = Path.Combine(_directory, id.ToString("N"));
        Directory.CreateDirectory(directory);

        // Online projects are loaded from their saved endpoint, then passed to the runner as an isolated snapshot.
        if (string.IsNullOrWhiteSpace(shortcut.ProjectFile))
        {
            request.ProjectFile = Path.Combine(directory, "project.fbprj");
            project.Save(request.ProjectFile);
        }
        else
        {
            request.ProjectFile = shortcut.ProjectFile;
        }
        request.ProjectSpaceGuid = null;

        var entry = new ProjectExecutionEntry
        {
            Id = id,
            Project = shortcut,
            Request = request,
            StartedUtc = DateTime.UtcNow,
            InputDirectory = inputDirectory,
            OutputDirectory = outputDirectory,
            RequestFile = Path.Combine(directory, "request.json"),
            ResponseFile = Path.Combine(directory, "response.json")
        };
        RunnerJson.WriteFile(entry.RequestFile, request);
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

    public bool Refresh()
    {
        var completed = new Dictionary<Guid, RunnerResponse>();
        foreach (var entry in _store.Read().Where(x => x.IsRunning && File.Exists(x.ResponseFile)))
        {
            try
            {
                var response = RunnerJson.ReadFile<RunnerResponse>(entry.ResponseFile);
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
        return true;
    }

    // Removing a shortcut never terminates its runner or deletes its request/response/log files.
    public void Remove(Guid id) => _store.Update(entries => entries.RemoveAll(x => x.Id == id));
}
