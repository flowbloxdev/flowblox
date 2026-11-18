using FlowBlox.Core.Models.Project.Launcher;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectArtifactsTests
{
    private string _directory = null!;
    [TestInitialize] public void Initialize() { _directory = Path.Combine(Path.GetTempPath(), "FlowBloxArtifacts-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    [TestMethod]
    public void List_PreservesFoldersAndCountsNestedArtifacts()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "album"));
        File.WriteAllText(Path.Combine(_directory, "album", "track.mp3"), "audio");
        File.WriteAllText(Path.Combine(_directory, "summary.md"), "summary");
        var artifacts = new ProjectArtifacts(_directory);
        Assert.IsTrue(artifacts.List()[0].IsDirectory);
        Assert.AreEqual("album/track.mp3", artifacts.List("album").Single().RelativePath);
        Assert.AreEqual(2, artifacts.Count());
    }

    [TestMethod]
    public void Clear_DeletesNestedArtifactsButKeepsOutputDirectoryAndOutsideFiles()
    {
        var output = Path.Combine(_directory, "output");
        Directory.CreateDirectory(Path.Combine(output, "nested"));
        File.WriteAllText(Path.Combine(output, "nested", "track.mp3"), "audio");
        var outside = Path.Combine(_directory, "keep.txt");
        File.WriteAllText(outside, "keep");
        Assert.AreEqual(1, new ProjectArtifacts(output).Clear());
        Assert.IsTrue(Directory.Exists(output));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(output));
        Assert.AreEqual("keep", File.ReadAllText(outside));
    }

    [TestMethod]
    public void OpenRead_RejectsTraversalToSiblingDirectory()
    {
        var output = Path.Combine(_directory, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(_directory, "secret.txt"), "secret");
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new ProjectArtifacts(output).OpenRead("../secret.txt"));
    }

    [TestMethod]
    public void List_RejectsAbsolutePaths()
    {
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new ProjectArtifacts(_directory).List(Path.GetTempPath()));
    }

    [TestMethod]
    public void Constructor_RejectsVolumeRoot()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ProjectArtifacts(Path.GetPathRoot(_directory)!));
    }

    [TestMethod]
    public void OpenRead_RejectsAlternateDataStreams()
    {
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new ProjectArtifacts(_directory).OpenRead("track.mp3:hidden"));
    }

    [TestMethod]
    public void ClearArtifacts_ProtectsTheOutputTreeOfAnActiveExecution()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var output = Path.Combine(_directory, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "keep.mp3"), "audio");
        var store = Path.Combine(_directory, "executions.json");
        new LauncherJsonStore<ProjectExecutionEntry>(store).Update(entries => entries.Add(new ProjectExecutionEntry
        {
            Id = Guid.NewGuid(), ProcessId = process.Id, ProcessStartedUtc = process.StartTime.ToUniversalTime(), OutputDirectory = output
        }));
        Assert.ThrowsExactly<InvalidOperationException>(() => new ProjectExecutions(store).ClearArtifacts(_directory));
        Assert.IsTrue(File.Exists(Path.Combine(output, "keep.mp3")));
    }
}
