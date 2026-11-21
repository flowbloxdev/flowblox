using System.Diagnostics;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectExecutionRecoveryTests
{
    private string _directory = null!;
    private string _history = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _history = Path.Combine(_directory, "history.json");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void AbandonedLaunchWithoutPidIsRemovedWithoutDeletingFiles()
    {
        var entry = CreateEntry();
        File.WriteAllText(entry.RequestFile, "{}");
        Save(entry);
        var executions = new ProjectExecutions(_history);
        Assert.IsTrue(executions.Refresh());
        Assert.IsEmpty(executions.GetAll());
        Assert.IsTrue(File.Exists(entry.RequestFile));
    }

    [TestMethod]
    public void FreshLaunchWithoutPidIsRetainedDuringStartup()
    {
        var entry = CreateEntry();
        entry.StartedUtc = DateTime.UtcNow;
        Save(entry);
        var executions = new ProjectExecutions(_history);
        Assert.IsFalse(executions.Refresh());
        Assert.IsTrue(executions.GetAll().Single().IsRunning);
    }

    [TestMethod]
    public void CompletedResponseWithoutPidIsPreservedInsteadOfDiscarded()
    {
        var entry = CreateEntry();
        Save(entry);
        RunnerJson.WriteFile(entry.ResponseFile, new RunnerResponse { Success = true, FinishedUtc = DateTime.UtcNow });
        var executions = new ProjectExecutions(_history);
        Assert.IsTrue(executions.Refresh());
        Assert.IsTrue(executions.GetAll().Single().Response!.Success);
    }

    [TestMethod]
    public void MissingProcessWithoutResponseBecomesFailureOnlyOnce()
    {
        var entry = CreateEntry();
        entry.ProcessId = int.MaxValue;
        entry.ProcessStartedUtc = entry.StartedUtc;
        Save(entry);
        var executions = new ProjectExecutions(_history);
        var completed = 0;
        executions.ExecutionCompleted += _ => completed++;
        Assert.IsTrue(executions.Refresh());
        Assert.IsFalse(executions.GetAll().Single().IsRunning);
        Assert.IsFalse(executions.GetAll().Single().Response!.Success);
        Assert.IsFalse(executions.Refresh());
        Assert.AreEqual(1, completed);
    }

    [TestMethod]
    public void LiveVerifiedProcessIsNotRecoveredOrRemoved()
    {
        using var process = Process.GetCurrentProcess();
        var entry = CreateEntry();
        entry.ProcessId = process.Id;
        entry.ProcessStartedUtc = process.StartTime.ToUniversalTime();
        Save(entry);
        var executions = new ProjectExecutions(_history);
        Assert.IsFalse(executions.Refresh());
        Assert.IsFalse(executions.RemoveInactive(entry.Id));
        Assert.IsTrue(executions.GetAll().Single().IsRunning);
    }

    [TestMethod]
    public void ReusedProcessIdFailsHistoryWithoutTouchingTheLiveProcess()
    {
        using var process = Process.GetCurrentProcess();
        var entry = CreateEntry();
        entry.ProcessId = process.Id;
        entry.ProcessStartedUtc = process.StartTime.ToUniversalTime().AddMinutes(-1);
        Save(entry);
        var executions = new ProjectExecutions(_history);
        Assert.IsTrue(executions.Refresh());
        Assert.IsFalse(executions.GetAll().Single().IsRunning);
        Assert.IsFalse(process.HasExited);
        Assert.IsTrue(executions.RemoveInactive(entry.Id));
        Assert.IsEmpty(executions.GetAll());
    }

    private ProjectExecutionEntry CreateEntry() => new()
    {
        Id = Guid.NewGuid(),
        StartedUtc = DateTime.UtcNow.AddMinutes(-5),
        RequestFile = Path.Combine(_directory, "request.json"),
        ResponseFile = Path.Combine(_directory, "response.json")
    };

    private void Save(ProjectExecutionEntry entry) => new LauncherJsonStore<ProjectExecutionEntry>(_history)
        .Update(entries => entries.Add(entry));
}
