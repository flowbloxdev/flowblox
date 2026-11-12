using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class DistributedDataHandlerTests
{
    [TestMethod]
    [DataRow("skill_project")]
    [DataRow("example_project")]
    [DataRow("demo_prompts")]
    public async Task List_ReturnsFilesThatCanBeResolved(string dataType)
    {
        var response = await Execute("GetDistributedData", new JObject { ["dataType"] = dataType });

        Assert.IsTrue(response.Ok, response.Error);
        var files = response.Result["dataTypes"]!.Single()!["fileNames"]!.Values<string>().ToArray();
        Assert.IsNotEmpty(files);
        foreach (var file in files)
        {
            var path = DistributedDataCatalog.ResolvePath(dataType, file);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(DistributedDataCatalog.IsDistributedDataPath(path));
        }
    }

    [TestMethod]
    public async Task Read_ReturnsContentsOfRequestedFile()
    {
        const string fileName = "export_onnx_qa_model.py";
        var response = await Execute("GetDistributedDataContent", new JObject
        {
            ["dataType"] = "python",
            ["fileName"] = fileName
        });

        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual(
            File.ReadAllText(DistributedDataCatalog.ResolvePath("python", fileName)),
            response.Result.Value<string>("content"));
    }

    [TestMethod]
    [DataRow("../export_onnx_qa_model.py")]
    [DataRow("..\\export_onnx_qa_model.py")]
    public async Task Read_RejectsPathTraversal(string fileName)
    {
        var response = await Execute("GetDistributedDataContent", new JObject
        {
            ["dataType"] = "python",
            ["fileName"] = fileName
        });

        Assert.IsFalse(response.Ok);
    }

    [TestMethod]
    public async Task DemoPrompts_ListOnlyNeutralFiles()
    {
        var response = await Execute("GetDistributedData", new JObject { ["dataType"] = "demo_prompts" });
        Assert.IsTrue(response.Ok, response.Error);
        var files = response.Result["dataTypes"]!.Single()!["fileNames"]!.Values<string>().ToArray();
        Assert.IsFalse(files.Any(file => file.EndsWith(".de.txt", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task DemoPrompts_AllowReadingLocalizedContent()
    {
        const string localizedFile = "filter-records-by-code.de.txt";
        var localized = await Execute("GetDistributedDataContent", new JObject
        {
            ["dataType"] = "demo_prompts",
            ["fileName"] = localizedFile
        });

        Assert.IsTrue(localized.Ok, localized.Error);
        Assert.AreEqual(
            File.ReadAllText(DistributedDataCatalog.ResolvePath("demo_prompts", localizedFile)),
            localized.Result.Value<string>("content"));
    }

    [TestMethod]
    public void UserProject_IsNotClassifiedAsDistributedData()
    {
        var path = Path.Combine(Path.GetTempPath(), "FlowBlox", "project.fbprj");
        Assert.IsFalse(DistributedDataCatalog.IsDistributedDataPath(path));
    }

    private static Task<ToolResponse> Execute(string toolName, JObject arguments) =>
        new DefaultToolApi().ExecuteAsync(new ToolRequest { ToolName = toolName, Arguments = arguments }, CancellationToken.None);
}
