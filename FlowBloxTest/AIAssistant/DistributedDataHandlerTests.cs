using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class DistributedDataHandlerTests
    {
        [TestMethod]
        public async Task DistributedData_CanBeListedAndReadButNotTraversed()
        {
            var api = new DefaultToolApi();
            var listed = await api.ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "GetDistributedData",
                    Arguments = new JObject { ["dataType"] = "skill_project" }
                },
                CancellationToken.None);
            var read = await api.ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "GetDistributedDataContent",
                    Arguments = new JObject
                    {
                        ["dataType"] = "python",
                        ["fileName"] = "export_onnx_qa_model.py"
                    }
                },
                CancellationToken.None);
            var exampleProjects = await api.ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "GetDistributedData",
                    Arguments = new JObject { ["dataType"] = "example_project" }
                },
                CancellationToken.None);
            var demoPrompts = await api.ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "GetDistributedData",
                    Arguments = new JObject { ["dataType"] = "demo_prompts" }
                },
                CancellationToken.None);
            var traversal = await api.ExecuteAsync(
                new ToolRequest
                {
                    ToolName = "GetDistributedDataContent",
                    Arguments = new JObject
                    {
                        ["dataType"] = "python",
                        ["fileName"] = "../export_onnx_qa_model.py"
                    }
                },
                CancellationToken.None);

            Assert.IsTrue(listed.Ok, listed.Error);
            var fileNames = listed.Result["dataTypes"]!
                .Single(x => x!.Value<string>("dataType") == "skill_project")!["fileNames"]!
                .Values<string>()
                .ToList();
            Assert.IsTrue(fileNames.Count > 0);
            Assert.IsTrue(fileNames.All(x => !string.IsNullOrWhiteSpace(x)));
            var distributedProjectFile = DistributedDataCatalog.ResolvePath("skill_project", fileNames[0]);
            Assert.IsTrue(DistributedDataCatalog.IsDistributedDataPath(distributedProjectFile));
            Assert.IsFalse(DistributedDataCatalog.IsDistributedDataPath(
                Path.Combine(Path.GetTempPath(), "FlowBlox", "project.fbprj")));
            Assert.IsTrue(read.Ok, read.Error);
            Assert.IsTrue(exampleProjects.Ok, exampleProjects.Error);
            Assert.IsTrue(demoPrompts.Ok, demoPrompts.Error);
            var demoPromptFileNames = demoPrompts.Result!["dataTypes"]!
                .Single()!["fileNames"]!
                .Values<string>()
                .ToList();
            CollectionAssert.Contains(demoPromptFileNames, "filter-records-by-code.txt");
            Assert.IsFalse(demoPromptFileNames.Any(x =>
                x?.EndsWith(".de.txt", StringComparison.OrdinalIgnoreCase) == true));
            var germanDemoPrompt = DistributedDataCatalog.ReadContent(
                DistributedDataCatalog.DemoPromptType,
                "filter-records-by-code.de.txt");
            StringAssert.Contains(germanDemoPrompt, "Datensätze");
            Assert.AreEqual(
                "example_project",
                exampleProjects.Result["dataTypes"]!.Single()!.Value<string>("dataType"));
            const string scraperFileName = "Test-Driven-BooksToScrape-Category-Scraper.fbprj";
            var exampleProjectFileNames = exampleProjects.Result["dataTypes"]!
                .Single()!["fileNames"]!
                .Values<string>()
                .ToList();
            CollectionAssert.Contains(exampleProjectFileNames, scraperFileName);
            var scraperProject = FlowBloxProject.FromFile(
                DistributedDataCatalog.ResolvePath("example_project", scraperFileName));
            Assert.AreEqual("Test-Driven BooksToScrape Category Scraper", scraperProject.ProjectName);
            StringAssert.Contains(scraperProject.ProjectDescription, "Offline SequenceDetection");
            StringAssert.Contains(read.Result.Value<string>("content"), "DEFAULT_MODEL_ROOT");
            Assert.IsFalse(traversal.Ok);
            StringAssert.Contains(traversal.Error, "plain filename");
        }
    }
}
