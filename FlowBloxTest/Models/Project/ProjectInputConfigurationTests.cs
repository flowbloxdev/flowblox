using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;

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
}
