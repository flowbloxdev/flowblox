using FlowBlox.Core.Models.FlowBlocks.Json;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectImportMigrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Import_LegacyPath_AppliesMigrationWithAndWithoutLocalFile(bool hasLocalFile)
    {
        const string json = """
            {
              "ProjectName": "Legacy import fixture",
              "FlowBlocks": [{
                "$type": "FlowBlox.Core.Models.FlowBlocks.Json.JPathSelectorFlowBlock, FlowBlox.Core",
                "Version": "1.0.0.0",
                "Name": "ReadValue",
                "Path": "/answer",
                "JsonContent": "{\"answer\":42}"
              }]
            }
            """;
        var projectSpaceGuid = hasLocalFile ? null : "56c79b9db7ee4a0097f74eb61f77fdb9";
        var project = FlowBloxProject.FromJsonContents(
            json,
            null,
            projectSpaceGuid: projectSpaceGuid,
            fileNameForAdjustments: hasLocalFile ? Path.Combine(Path.GetTempPath(), "migration.fbprj") : null);

        var selector = project.FlowBlocks.OfType<JPathSelectorFlowBlock>().Single();
        Assert.AreEqual(42, JObject.Parse(selector.JsonContent).SelectToken(selector.Path)?.Value<int>());
        Assert.AreEqual(projectSpaceGuid, project.ProjectSpaceGuid);
    }
}
