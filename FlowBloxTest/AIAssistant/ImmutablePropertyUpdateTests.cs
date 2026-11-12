using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Communication;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ImmutablePropertyUpdateTests
{
    [TestMethod]
    [DataRow("/PropertySelectedFieldMap")]
    [DataRow("PropertySelectedFieldMap")]
    [DataRow("/PropertySelectedFieldMap/Port")]
    [DataRow("PropertySelectedFieldMap.Port")]
    [DataRow("/PropertySelectedFieldMap/Delete")]
    [DataRow("/PropertySelectedFieldMap/Link")]
    [DataRow("/PropertySelectedFieldMap/Unlink")]
    public async Task BothUpdateHandlers_RejectMapWritesAndPreserveAssignments(string path)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        try
        {
            var block = new SMTPFlowBlock { Name = "Mail" };
            project.FlowBloxRegistry.RegisterFlowBlock(block);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Target");
            var targets = new (FlowBloxReactiveObject Target, string Name, ToolHandlerBase Handler)[]
            {
                (block, block.Name, new UpdateFlowBlockHandler()),
                (field, field.FullyQualifiedName, new UpdateManagedObjectHandler())
            };

            foreach (var target in targets)
            {
                target.Target.SetPropertySelectedFieldName("Port", "$User::Original");
                var response = await target.Handler.HandleAsync(new JObject
                {
                    ["name"] = target.Name,
                    ["path"] = path,
                    ["value"] = new JObject { ["Port"] = "$User::Changed" }
                }, CancellationToken.None);

                Assert.IsFalse(response.Ok);
                StringAssert.Contains(response.Error, "read-only");
                StringAssert.Contains(response.Error, nameof(FlowBloxReactiveObject.PropertySelectedFieldMap));
                Assert.AreEqual(1, target.Target.PropertySelectedFieldMap.Count);
                Assert.AreEqual("$User::Original", target.Target.GetPropertySelectedFieldName("Port"));
            }
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    [DataRow("/FixedValue")]
    [DataRow("/FixedSettings/Limit")]
    public void ImmutableAttribute_BlocksArbitraryPropertiesAndDescendants(string path)
    {
        var project = new FlowBloxProject();
        var target = new ImmutablePropertyFixture();
        var response = ToolHandlerUtilities.ApplyUpdates(new JObject
        {
            ["path"] = path,
            ["value"] = "Changed"
        }, target, project.FlowBloxRegistry, "reactiveObject", "Fixture");

        Assert.IsFalse(response.Ok);
        StringAssert.Contains(response.Error, "read-only");
        Assert.AreEqual("Original", target.FixedValue);
        Assert.AreEqual(10, target.FixedSettings.Limit);
    }

    [TestMethod]
    public async Task Snapshot_ExposesCurrentScalarFieldAssignments()
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        try
        {
            var block = new SMTPFlowBlock { Name = "Mail" };
            block.SetPropertySelectedFieldName(nameof(block.Port), "$User::MailPort");
            project.FlowBloxRegistry.RegisterFlowBlock(block);
            var snapshot = await new GetComponentSnapshotHandler().HandleAsync(new JObject
            {
                ["kind"] = "flowBlock",
                ["name"] = block.Name
            }, CancellationToken.None);

            Assert.IsTrue(snapshot.Ok, snapshot.Error);
            Assert.AreEqual("$User::MailPort", snapshot.Result["snapshot"]?
                [nameof(block.PropertySelectedFieldMap)]?[nameof(block.Port)]?.Value<string>());

        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }
}
