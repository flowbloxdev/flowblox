using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
[DoNotParallelize]
public sealed class DashboardRunnerSnapshotTests
{
    [TestMethod]
    public void ImportedDashboardProject_PreservesStartGraphAndInputsWhenSavingRunnerSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxDashboardSnapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var activeProject = FlowBloxProjectManager.Instance.ActiveProject;
        try
        {
            var start = new StartFlowBlock { Name = "Start" };
            var format = new FormatFlowBlock { Name = "Format", FormatExpression = "Result" };
            format.ReferencedFlowBlocks.Add(start);
            var source = new FlowBloxProject
            {
                ProjectName = "Imported dashboard project",
                FlowBlocks = [start, format],
                UserFields = [new FieldElement
                {
                    Name = "Input",
                    UserField = true,
                    UserFieldType = UserFieldTypes.Input,
                    FieldType = new TypeElement { FieldType = FieldTypes.Text },
                    DefaultValue = "Default",
                    StringValue = "Local override"
                }]
            };
            var json = JsonConvert.SerializeObject(source, JsonSettings.ProjectExport());
            var imported = FlowBloxProject.FromJsonContents(json, null);
            Assert.IsEmpty(imported.FlowBloxRegistry.GetFlowBlocks());
            var path = Path.Combine(directory, "project.fbprj");

            ProjectExecutions.SaveRunnerSnapshot(imported, path);

            var snapshot = FlowBloxProject.FromFile(path);
            var loadedStart = snapshot.FlowBlocks.OfType<StartFlowBlock>().Single();
            Assert.AreEqual("Start", loadedStart.Name);
            Assert.AreSame(loadedStart, snapshot.FlowBlocks.OfType<FormatFlowBlock>().Single().ReferencedFlowBlocks.Single());
            Assert.AreEqual("Default", snapshot.UserFields.Single(field => field.Name == "Input").StringValue);
            Assert.AreSame(activeProject, FlowBloxProjectManager.Instance.ActiveProject);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
