using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectInputConfigurationTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => _directory = Path.Combine(Path.GetTempPath(), "FlowBloxInputConfigurations", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void PartialConfiguration_RoundTripsSecretsProtected_WithoutExecuting()
    {
        var path = Path.Combine(_directory, "inputs.json");
        var project = new ProjectShortcut { ProjectFile = Path.Combine(_directory, "project.fbprj") };
        new ProjectInputConfigurations(path).Save(new ProjectInputConfiguration
        {
            Name = " Research inputs ",
            Project = project,
            Request = new RunnerRequest
            {
                PasswordFieldNames = ["Password"],
                UserFields = new() { ["Question"] = "", ["Password"] = "saved-configuration-secret", ["UserName"] = "$Vault::UserName" },
                OptionOverrides = new() { ["Paths.OutputDir"] = "configured-output" }
            }
        });

        Assert.DoesNotContain("saved-configuration-secret", File.ReadAllText(path));
        var restored = new ProjectInputConfigurations(path).GetForProject(project).Single();
        Assert.AreEqual("Research inputs", restored.Name);
        Assert.AreEqual("", restored.Request.UserFields["Question"]);
        Assert.AreEqual("saved-configuration-secret", restored.Request.UserFields["Password"]);
        Assert.AreEqual("$Vault::UserName", restored.Request.UserFields["UserName"]);
        Assert.AreEqual("configured-output", restored.Request.OptionOverrides["Paths.OutputDir"]);
    }

    [TestMethod]
    public void Rename_ChangesOnlyTheName_AndRejectsAnotherProjectsConfiguration()
    {
        var store = new ProjectInputConfigurations(Path.Combine(_directory, "inputs.json"));
        var project = new ProjectShortcut { ProjectFile = Path.Combine(_directory, "first.fbprj") };
        var other = new ProjectShortcut { ProjectFile = Path.Combine(_directory, "other.fbprj") };
        var saved = new ProjectInputConfiguration
        {
            Name = "Before",
            Project = project,
            Request = new RunnerRequest { UserFields = new() { ["Input"] = "keep value" } }
        };
        store.Save(saved);
        Assert.IsEmpty(store.GetForProject(other));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Rename(other, saved.Id, "Wrong project"));
        store.Rename(project, saved.Id, " After ");
        var renamed = store.GetForProject(project).Single();
        Assert.AreEqual(saved.Id, renamed.Id);
        Assert.AreEqual("After", renamed.Name);
        Assert.AreEqual("keep value", renamed.Request.UserFields["Input"]);
    }

    [TestMethod]
    public void Status_UsesCurrentDefaultsAndRequirements_AndIgnoresRemovedFields()
    {
        var saved = new ProjectInputConfiguration
        {
            Request = new RunnerRequest { UserFields = new() { ["Question"] = "", ["Removed"] = "old field", ["Provider"] = "" } }
        };
        var fields = new[]
        {
            new FieldElement(FieldTypes.Text) { Name = "Question", IsRequiredAtRuntimeStartup = true },
            new FieldElement(FieldTypes.Text) { Name = "Provider", DefaultValue = "CUDA", IsRequiredAtRuntimeStartup = true },
            new FieldElement(FieldTypes.Text) { Name = "Password", IsPassword = true }
        };
        var status = saved.GetStatus(fields);
        Assert.AreEqual(1, status.FilledCount);
        Assert.AreEqual(3, status.TotalCount);
        CollectionAssert.AreEqual(new[] { "Question" }, status.MissingRequiredFields.ToArray());
        Assert.AreEqual("CUDA", saved.GetInputValue(fields[1]));
    }

    [TestMethod]
    public void LocalConfiguration_IsDetachedAndReloadsCurrentSidecarValues()
    {
        var project = WriteLocalProject(new() { ["Input"] = "$Options::Paths.InputDir/source.txt" });
        var store = new ProjectInputConfigurations(Path.Combine(_directory, "inputs.json"));
        store.Save(new ProjectInputConfiguration { Name = "Saved preset", Project = project });
        var profiles = store.GetForProject(project);
        var local = profiles[0];

        Assert.IsTrue(local.IsReadOnly);
        Assert.AreEqual("$Options::Paths.InputDir/source.txt", local.Request.UserFields["Input"]);
        local.Request.UserFields["Input"] = "run-only edit";
        Assert.AreEqual("$Options::Paths.InputDir/source.txt", store.GetForProject(project)[0].Request.UserFields["Input"]);
        Assert.IsFalse(profiles[1].IsReadOnly);

        WriteLocalProject(new() { ["Input"] = "updated developer value" });
        var refreshed = store.GetForProject(project)[0];
        Assert.AreEqual(local.Id, refreshed.Id);
        Assert.AreEqual("updated developer value", refreshed.Request.UserFields["Input"]);
    }

    [TestMethod]
    public void LocalConfiguration_RejectsSaveAndRenameWithoutWritingProjectOrPresetData()
    {
        var project = WriteLocalProject(new() { ["Input"] = "developer value" });
        var sidecar = Path.ChangeExtension(project.ProjectFile, ".fblocaldata");
        var projectBytes = File.ReadAllBytes(project.ProjectFile);
        var sidecarBytes = File.ReadAllBytes(sidecar);
        var path = Path.Combine(_directory, "inputs.json");
        var store = new ProjectInputConfigurations(path);
        var local = store.GetForProject(project).Single();
        local.Name = "Attempted rename";
        local.Request.UserFields["Input"] = "run-only edit";

        Assert.ThrowsExactly<InvalidOperationException>(() => store.Save(local));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Rename(project, local.Id, "Another name"));

        CollectionAssert.AreEqual(projectBytes, File.ReadAllBytes(project.ProjectFile));
        CollectionAssert.AreEqual(sidecarBytes, File.ReadAllBytes(sidecar));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void LocalConfiguration_RunEditsCanBeSavedAsANewProtectedPreset()
    {
        var project = WriteLocalProject(new() { ["Input"] = "developer value", ["Password"] = "local-fixture-secret" });
        var sidecar = Path.ChangeExtension(project.ProjectFile, ".fblocaldata");
        var original = File.ReadAllBytes(sidecar);
        var path = Path.Combine(_directory, "inputs.json");
        var store = new ProjectInputConfigurations(path);
        var local = store.GetForProject(project).Single();
        var runValues = new Dictionary<string, string>(local.Request.UserFields) { ["Input"] = "edited run value" };
        store.Save(new ProjectInputConfiguration
        {
            Name = "My copy",
            Project = project,
            Request = new RunnerRequest { UserFields = runValues, PasswordFieldNames = ["Password"] }
        });

        var saved = store.GetForProject(project).Single(profile => !profile.IsReadOnly);
        Assert.AreEqual("edited run value", saved.Request.UserFields["Input"]);
        Assert.AreEqual("local-fixture-secret", saved.Request.UserFields["Password"]);
        Assert.DoesNotContain("local-fixture-secret", File.ReadAllText(path));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(sidecar));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void WithoutLocalSidecar_NoDeveloperConfigurationIsOffered(bool hasLocalFile)
    {
        Directory.CreateDirectory(_directory);
        var project = new ProjectShortcut
        {
            ProjectFile = hasLocalFile ? Path.Combine(_directory, "project.fbprj") : string.Empty,
            ProjectSpaceGuid = Guid.NewGuid().ToString("N"),
            ApiEndpoint = "https://example.test/api"
        };
        if (hasLocalFile)
            File.WriteAllText(project.ProjectFile, "{}");

        var store = new ProjectInputConfigurations(Path.Combine(_directory, "inputs.json"));
        Assert.IsEmpty(store.GetForProject(project));
    }

    [TestMethod]
    public void LocalConfiguration_IsOfferedOnlyWhileSidecarContainsAConfiguredValue()
    {
        var project = WriteLocalProject(new());
        var store = new ProjectInputConfigurations(Path.Combine(_directory, "inputs.json"));
        store.Save(new ProjectInputConfiguration { Name = "Saved preset", Project = project });

        Assert.IsFalse(store.GetForProject(project).Single().IsReadOnly);
        WriteLocalProject(new() { ["Input"] = "  ", ["Other"] = "" });
        Assert.IsFalse(store.GetForProject(project).Single().IsReadOnly);

        WriteLocalProject(new() { ["Enabled"] = "false" });
        Assert.IsTrue(store.GetForProject(project)[0].IsReadOnly);
        Assert.AreEqual("false", store.GetForProject(project)[0].Request.UserFields["Enabled"]);

        WriteLocalProject(new());
        Assert.IsFalse(store.GetForProject(project).Single().IsReadOnly);
    }

    private ProjectShortcut WriteLocalProject(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(_directory);
        var project = new ProjectShortcut
        {
            Name = "Local fixture",
            ProjectFile = Path.Combine(_directory, "project.fbprj"),
            ApiEndpoint = "https://example.test/api"
        };
        File.WriteAllText(project.ProjectFile, "{\"ProjectName\":\"Local fixture\"}");
        File.WriteAllText(
            Path.ChangeExtension(project.ProjectFile, ".fblocaldata"),
            JsonConvert.SerializeObject(new FlowBloxProjectLocalData { LocalUserFieldValues = values }));
        return project;
    }
}
