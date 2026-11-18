using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class ProjectExecutionHistoryTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxHistoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void LocalHistory_SeparatesSameNamedFilesAndSortsNewestFirst()
    {
        var file = Path.Combine(_directory, "one.fbprj");
        var target = new ProjectShortcut { Name = "Same name", ProjectFile = file };
        var latest = new ProjectExecutionEntry { Project = target, StartedUtc = DateTime.UtcNow };
        var older = new ProjectExecutionEntry { Project = target, StartedUtc = latest.StartedUtc.AddHours(-1) };
        var unrelated = new ProjectExecutionEntry
        {
            Project = new ProjectShortcut { Name = target.Name, ProjectFile = Path.Combine(_directory, "two.fbprj") },
            StartedUtc = latest.StartedUtc.AddHours(1)
        };
        var path = Path.Combine(_directory, "history.json");
        new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries => entries.AddRange([older, unrelated, latest]));

        var history = new ProjectExecutions(path).GetForProject(target);

        Assert.AreEqual(2, history.Count);
        Assert.AreEqual(latest.StartedUtc, history[0].StartedUtc);
        Assert.AreEqual(older.StartedUtc, history[1].StartedUtc);
    }

    [TestMethod]
    public void RemoteHistory_RequiresMatchingEndpointAndGuid()
    {
        var guid = Guid.NewGuid().ToString();
        var target = new ProjectShortcut { ProjectSpaceGuid = guid, ApiEndpoint = "https://one.example/api" };
        var matching = new ProjectShortcut { ProjectSpaceGuid = guid.ToUpperInvariant(), ApiEndpoint = target.ApiEndpoint + "/" };
        var otherServer = new ProjectShortcut { ProjectSpaceGuid = guid, ApiEndpoint = "https://two.example/api" };
        var localCopy = new ProjectShortcut { ProjectSpaceGuid = guid, ApiEndpoint = target.ApiEndpoint, ProjectFile = Path.Combine(_directory, "copy.fbprj") };

        Assert.IsTrue(ProjectShortcutIdentity.Matches(target, matching));
        Assert.IsFalse(ProjectShortcutIdentity.Matches(target, otherServer));
        Assert.IsFalse(ProjectShortcutIdentity.Matches(target, localCopy));
        Assert.IsFalse(ProjectShortcutIdentity.Matches(new ProjectShortcut(), new ProjectShortcut()));
    }

    [TestMethod]
    public void RequestTransfer_ProtectsOnlyMarkedPasswordsAndKeepsOriginalInputsUnchanged()
    {
        var request = new RunnerRequest
        {
            PasswordFieldNames = ["secret"],
            UserFields = new() { ["Secret"] = "private-test-value", ["Query"] = "readable-input" },
            OptionOverrides = new() { ["Runtime.StepTimeunit"] = "20" }
        };
        var path = Path.Combine(_directory, "request.json");

        RunnerJson.WriteProtectedRequestFile(path, request);

        var stored = File.ReadAllText(path);
        Assert.DoesNotContain("private-test-value", stored);
        Assert.Contains("readable-input", stored);
        Assert.Contains("Runtime.StepTimeunit", stored);
        Assert.AreEqual("private-test-value", request.UserFields["Secret"]);
        var restored = RunnerJson.ReadFile<RunnerRequest>(path);
        Assert.AreEqual("private-test-value", restored.UserFields["Secret"]);
        Assert.AreEqual("readable-input", restored.UserFields["Query"]);
    }

    [TestMethod]
    public void HistoryStore_RestoresPasswordsWhilePersistingOrdinaryInputsAsReadableJson()
    {
        var path = Path.Combine(_directory, "history.json");
        var entry = new ProjectExecutionEntry
        {
            Request = new RunnerRequest
            {
                PasswordFieldNames = ["Password"],
                UserFields = new() { ["Password"] = "history-private-value", ["Count"] = "normal-debug-value" }
            }
        };
        new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries => entries.Add(entry));

        var stored = File.ReadAllText(path);
        Assert.DoesNotContain("history-private-value", stored);
        Assert.Contains("normal-debug-value", stored);
        Assert.AreEqual("history-private-value", new ProjectExecutions(path).GetAll().Single().Request.UserFields["Password"]);
    }

    [TestMethod]
    public void LegacyPlaintextRequests_RemainReadableWithoutPasswordMetadata()
    {
        var request = new RunnerRequest { UserFields = new() { ["Input"] = "legacy-value" } };
        var requestPath = Path.Combine(_directory, "legacy-request.json");
        RunnerJson.WriteFile(requestPath, request);
        var historyPath = Path.Combine(_directory, "legacy-history.json");
        File.WriteAllText(historyPath, "[{\"Request\":" + JsonConvert.SerializeObject(request) + "}]");

        Assert.AreEqual("legacy-value", RunnerJson.ReadFile<RunnerRequest>(requestPath).UserFields["Input"]);
        Assert.AreEqual("legacy-value", new ProjectExecutions(historyPath).GetAll().Single().Request.UserFields["Input"]);
    }
}
