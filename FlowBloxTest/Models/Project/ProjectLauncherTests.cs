using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class ProjectLauncherTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void Favorite_IdentifiesProjectsByGuidAndEndpointWithoutCreatingLocalReference()
    {
        var path = Path.Combine(_directory, "favorites.json");
        var store = new ProjectSpaceFavorites(path);
        var guid = Guid.NewGuid().ToString();
        store.SetFavorite("https://one.example/api/", guid, "First", true);
        store.SetFavorite("https://one.example/api", guid.ToUpperInvariant(), "Updated", true);
        store.SetFavorite("https://two.example/api", guid, "Other endpoint", true);

        var reloaded = new ProjectSpaceFavorites(path);
        Assert.AreEqual(2, reloaded.GetAll().Count);
        Assert.IsTrue(reloaded.GetAll().All(x => string.IsNullOrEmpty(x.ProjectFile)));
        reloaded.SetFavorite("https://one.example/api/", guid, "", false);
        Assert.IsFalse(reloaded.Contains("https://one.example/api", guid));
        Assert.IsTrue(reloaded.Contains("https://two.example/api", guid));
    }

    [TestMethod]
    public void Shortcut_ReadsProjectSpaceIdentityFromLocalSidecar()
    {
        var path = Path.Combine(_directory, "local.fbprj");
        var guid = Guid.NewGuid().ToString();
        File.WriteAllText(path, "{\"ProjectName\":\"Local project\"}");
        File.WriteAllText(Path.ChangeExtension(path, ".fblocaldata"), JsonConvert.SerializeObject(new
        {
            ProjectSpaceGuid = guid,
            ProjectSpaceEndpointUri = "https://projects.example/api"
        }));

        var shortcut = ProjectShortcut.FromFile(path);

        Assert.AreEqual(Path.GetFullPath(path), shortcut.ProjectFile);
        Assert.AreEqual(guid, shortcut.ProjectSpaceGuid);
        Assert.AreEqual("https://projects.example/api", shortcut.ApiEndpoint);
    }

    [TestMethod]
    public void Refresh_LeavesPartialResponseRunningAndDetectsCompletionAfterRestart()
    {
        var path = Path.Combine(_directory, "executions.json");
        var responsePath = Path.Combine(_directory, "response.json");
        var entry = new ProjectExecutionEntry
        {
            Id = Guid.NewGuid(),
            ResponseFile = responsePath,
            StartedUtc = DateTime.UtcNow
        };
        new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries => entries.Add(entry));
        File.WriteAllText(responsePath, "{\"Success\":");
        var store = new ProjectExecutions(path);

        Assert.IsFalse(store.Refresh());
        Assert.IsTrue(store.GetAll().Single().IsRunning);
        RunnerJson.WriteFile(responsePath, new RunnerResponse
        {
            Success = false,
            ErrorMessage = "Project validation failed",
            FinishedUtc = DateTime.UtcNow
        });

        var restarted = new ProjectExecutions(path);
        Assert.IsTrue(restarted.Refresh());
        Assert.IsFalse(restarted.GetAll().Single().IsRunning);
        Assert.AreEqual("Project validation failed", restarted.GetAll().Single().Response!.ErrorMessage);
        Assert.IsFalse(restarted.Refresh());
    }

    [TestMethod]
    public void RemoveRunningExecution_OnlyRemovesListEntryAndPreservesRunnerFiles()
    {
        var path = Path.Combine(_directory, "executions.json");
        var requestPath = Path.Combine(_directory, "request.json");
        File.WriteAllText(requestPath, "{}");
        var entry = new ProjectExecutionEntry { Id = Guid.NewGuid(), RequestFile = requestPath };
        new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries => entries.Add(entry));

        new ProjectExecutions(path).Remove(entry.Id);

        Assert.AreEqual(0, new ProjectExecutions(path).GetAll().Count);
        Assert.IsTrue(File.Exists(requestPath));
    }

    [TestMethod]
    public void DamagedFavorites_AreNotSilentlyReplacedWhenAddingFavorite()
    {
        var path = Path.Combine(_directory, "favorites.json");
        const string damagedContent = "[{broken";
        File.WriteAllText(path, damagedContent);
        var store = new ProjectSpaceFavorites(path);

        Assert.ThrowsExactly<JsonReaderException>(() => store.SetFavorite(
            "https://projects.example", Guid.NewGuid().ToString(), "Project", true));

        Assert.AreEqual(damagedContent, File.ReadAllText(path));
    }
}
