using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AiAssistantSnapshotExportTests
{
    [TestMethod]
    public void ProjectAiExport_OmitsCanvasSettings_WhilePreservingNormalPersistence()
    {
        var project = new FlowBloxProject
        {
            ProjectName = "Synthetic snapshot",
            GridSizeX = 2000,
            GridSizeY = 1000,
            AutoIncreaseGridSize = true
        };
        var persisted = JObject.Parse(JsonConvert.SerializeObject(project, JsonSettings.ProjectExport()));
        var assistant = JObject.Parse(JsonConvert.SerializeObject(project, JsonSettings.ProjectExportForAiAssistant()));

        foreach (var name in new[] { "GridSizeX", "GridSizeY", "AutoIncreaseGridSize" })
        {
            Assert.IsNotNull(persisted[name], name);
            Assert.IsNull(assistant[name], name);
        }

        Assert.AreEqual(project.ProjectName, assistant.Value<string>("ProjectName"));
    }

    [TestMethod]
    public void PasswordFieldSnapshot_DoesNotExposeTheValueThroughItsUiPreview()
    {
        var field = new FieldElement
        {
            Name = "Secret",
            UserField = true,
            UserFieldType = UserFieldTypes.Memory,
            IsPassword = true,
            StringValue = "synthetic-secret-value"
        };
        var json = JsonConvert.SerializeObject(field, JsonSettings.ComponentSnapshotExport());
        var snapshot = JObject.Parse(json);

        Assert.IsNull(snapshot["StringValue"]);
        Assert.IsNull(snapshot["ShortStringValue"]);
        Assert.IsFalse(json.Contains("synthetic-secret-value", StringComparison.Ordinal));
        Assert.AreEqual(field.FullyQualifiedName, snapshot.Value<string>("FullyQualifiedName"));
        Assert.IsTrue(snapshot.Value<bool>("IsPassword"));
    }

    [TestMethod]
    public void MemoryFieldSnapshot_KeepsTheFullValueWithoutItsTruncatedDuplicate()
    {
        var field = new FieldElement
        {
            Name = "Payload",
            UserField = true,
            UserFieldType = UserFieldTypes.Memory,
            StringValue = new string('x', 150)
        };
        var snapshot = JObject.Parse(JsonConvert.SerializeObject(field, JsonSettings.ComponentSnapshotExport()));

        Assert.AreEqual(field.StringValue, snapshot.Value<string>("StringValue"));
        Assert.IsNull(snapshot["ShortStringValue"]);
    }
}
