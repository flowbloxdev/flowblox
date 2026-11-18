using System.Diagnostics;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Models.Runtime.Debugging;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExecutionProcessTerminationTests
{
    [TestMethod]
    public void Terminate_StopsMatchingRunnerAndPersistsUserCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTermination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var process = StartTestProcess();
        try
        {
            var path = Path.Combine(directory, "executions.json");
            var entry = CreateEntry(process, directory);
            new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries => entries.Add(entry));
            var executions = new ProjectExecutions(path);
            Assert.IsTrue(executions.CanTerminate(entry));

            Assert.IsTrue(executions.Terminate(entry.Id));

            Assert.IsTrue(process.WaitForExit(5000));
            var completed = new ProjectExecutions(path).GetAll().Single();
            Assert.IsFalse(completed.IsRunning);
            Assert.AreEqual(RuntimeCancellationKind.UserCancellation, completed.Response!.CancellationKind);
            Assert.IsFalse(executions.CanTerminate(completed));
            Assert.IsFalse(executions.Terminate(entry.Id));
        }
        finally
        {
            StopTestProcess(process);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void UnknownOrReusedProcessIdentity_DoesNotTerminateTheLiveProcess(bool missingStartTime)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxTermination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var process = StartTestProcess();
        try
        {
            var path = Path.Combine(directory, "executions.json");
            var entry = CreateEntry(process, directory);
            entry.ProcessStartedUtc = missingStartTime ? null : entry.ProcessStartedUtc!.Value.AddSeconds(-1);
            new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries => entries.Add(entry));
            var executions = new ProjectExecutions(path);

            Assert.IsFalse(executions.CanTerminate(entry));
            Assert.IsFalse(executions.Terminate(entry.Id));
            Assert.IsFalse(process.HasExited);
            Assert.IsTrue(executions.GetAll().Single().IsRunning);
        }
        finally
        {
            StopTestProcess(process);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExitedProcess_IsNotOfferedForTermination()
    {
        using var process = StartTestProcess();
        var entry = CreateEntry(process, Path.GetTempPath());
        StopTestProcess(process);

        Assert.IsFalse(new ProjectExecutions(Path.Combine(Path.GetTempPath(), "unused.json")).CanTerminate(entry));
    }

    private static ProjectExecutionEntry CreateEntry(Process process, string directory) => new()
    {
        Id = Guid.NewGuid(),
        ProcessId = process.Id,
        ProcessStartedUtc = process.StartTime.ToUniversalTime(),
        StartedUtc = DateTime.UtcNow,
        ResponseFile = Path.Combine(directory, "response.json")
    };

    private static Process StartTestProcess() => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60",
        UseShellExecute = false,
        CreateNoWindow = true
    })!;

    private static void StopTestProcess(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
}
