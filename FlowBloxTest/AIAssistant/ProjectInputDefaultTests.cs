using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Runner;
using FlowBlox.Core.Runner.Contracts;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectInputDefaultTests
{
    [TestMethod]
    public void MissingInputs_StayOmittedAndMetadataExposesOnlyProjectDefaults()
    {
        var project = new FlowBloxProject
        {
            FlowBlocks = [],
            UserFields =
            [
                CreateInput("Input", "Default", "Local override"),
                CreateInput("Password", null, "Local password", password: true)
            ]
        };

        var inputs = ProjectExecutionToolUtilities.ResolveInputParameters(null, project);
        var metadata = SkillProjectMetadataReader.Read(project, "project.fbprj");

        Assert.IsEmpty(inputs);
        Assert.AreEqual("Default", metadata.InputFieldMetadata["Input"].DefaultValue);
        Assert.IsNull(metadata.InputFieldMetadata["Password"].DefaultValue);
    }

    [TestMethod]
    [DataRow(null, "")]
    [DataRow("", "")]
    [DataRow("Explicit", "Explicit")]
    public void InputResolution_PreservesExplicitValuesForRuntimeFallback(string? value, string expected)
    {
        var project = new FlowBloxProject { UserFields = [CreateInput("Input", "Default", "Local")] };

        var inputs = ProjectExecutionToolUtilities.ResolveInputParameters(new JObject { ["Input"] = value }, project);

        Assert.AreEqual(expected, inputs["Input"]);
    }

    [TestMethod]
    public void InputResolution_FalseAndZeroOverrideDefaults()
    {
        var project = new FlowBloxProject
        {
            UserFields = [CreateInput("Boolean", "True", null), CreateInput("Number", "42", null)]
        };

        var inputs = ProjectExecutionToolUtilities.ResolveInputParameters(
            new JObject { ["Boolean"] = false, ["Number"] = 0 }, project);

        Assert.AreEqual("False", inputs["Boolean"]);
        Assert.AreEqual("0", inputs["Number"]);
    }

    [TestMethod]
    [DataRow(null, "Default")]
    [DataRow("", "Default")]
    [DataRow("Explicit", "Explicit")]
    public void Runner_UsesDefaultForMissingOrEmptyParametersInsteadOfSavedLocalOverride(string? value, string expected)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var directory = Path.Combine(Environment.CurrentDirectory, "TestResults", "runner-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = new FlowBloxProject { ProjectName = "Input defaults" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var registry = project.FlowBloxRegistry;
            var start = registry.CreateFlowBlockUnregistered<StartFlowBlock>();
            registry.PostProcessFlowBlockCreated(start);
            registry.Register(start);
            var output = registry.CreateFlowBlockUnregistered<ProjectOutputFlowBlock>();
            registry.PostProcessFlowBlockCreated(output);
            registry.Register(output);
            output.ReferencedFlowBlocks.Add(start);
            var field = registry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Input");
            field.DefaultValue = "Default";
            field.StringValue = "Local override";
            output.MappingEntries.Add(new ProjectOutputMappingEntry { Field = field, OutputPropertyName = "Text" });
            var path = Path.Combine(directory, "project.fbprj");
            project.Save(path);
            var request = new RunnerRequest { ProjectFile = path };
            if (value != null)
                request.UserFields["Input"] = value;

            var response = FlowBloxProjectRunner.Run(request);

            Assert.IsTrue(response.Success, response.Exception ?? response.ErrorMessage);
            Assert.AreEqual(expected, response.Outputs.Values.Single().Single().Values["Text"]);
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FieldElement CreateInput(string name, string? defaultValue, string? localValue, bool password = false) => new(FieldTypes.Text)
    {
        Name = name,
        UserField = true,
        UserFieldType = UserFieldTypes.Input,
        IsPassword = password,
        DefaultValue = defaultValue,
        StringValue = localValue
    };
}
