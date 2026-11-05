using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Provider.Project;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class CreateUserFieldHandlerTests
{
    [TestMethod]
    [DataRow("Memory", true, true, false, "Suggestion")]
    [DataRow("Input", false, true, false, "Suggestion")]
    [DataRow("Memory", false, false, true, "Suggestion")]
    [DataRow("Input", false, false, false, "Unknown")]
    public async Task InvalidStartupMetadata_DoesNotLeaveAnUnconfiguredField(
        string kind, bool required, bool clear, bool password, string listType)
    {
        var previous = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        try
        {
            var response = await new CreateUserFieldHandler().HandleAsync(new JObject
            {
                ["userFieldType"] = kind,
                ["name"] = "Invalid",
                ["isRequiredAtRuntimeStartup"] = required,
                ["clearOnRuntimeStart"] = clear,
                ["isPassword"] = password,
                ["loVType"] = listType
            }, CancellationToken.None);

            Assert.IsFalse(response.Ok);
            Assert.HasCount(0, project.FlowBloxRegistry.GetUserFields());
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previous;
        }
    }

    [TestMethod]
    public async Task CreatedValueContract_IsEnforcedByRuntimeStartupValidation()
    {
        var previous = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        try
        {
            var response = await new CreateUserFieldHandler().HandleAsync(new JObject
            {
                ["userFieldType"] = "Input",
                ["name"] = "Choice",
                ["isRequiredAtRuntimeStartup"] = true,
                ["loVType"] = "PossibleValues",
                ["listOfValues"] = new JArray(new JObject { ["value"] = "Allowed" })
            }, CancellationToken.None);
            Assert.IsTrue(response.Ok, response.Error);
            var field = project.FlowBloxRegistry.GetUserFields().Single();
            var start = project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
            project.FlowBloxRegistry.PostProcessFlowBlockCreated(start);
            project.FlowBloxRegistry.Register(start);
            var runtime = new TransientRuntime(project);
            Assert.ThrowsExactly<ValidationException>(runtime.ValidateUserFieldsAtRuntimeStartup);
            field.StringValue = "Other";
            Assert.ThrowsExactly<ValidationException>(runtime.ValidateUserFieldsAtRuntimeStartup);
            field.StringValue = "Allowed";
            runtime.ValidateUserFieldsAtRuntimeStartup();
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previous;
        }
    }
}
