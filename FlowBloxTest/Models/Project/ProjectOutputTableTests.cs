using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Util.Projects;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectOutputTableTests
{
    [TestMethod]
    public void CompletedExecution_PreservesOutputValuesWhenPersistedToHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBlox-output-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var responseFile = Path.Combine(directory, "response.json");
            var storePath = Path.Combine(directory, "history.json");
            var entry = new ProjectExecutionEntry { ResponseFile = responseFile };
            new LauncherJsonStore<ProjectExecutionEntry>(storePath).Update(entries => entries.Add(entry));
            RunnerJson.WriteFile(responseFile, new RunnerResponse
            {
                Success = true,
                FinishedUtc = DateTime.UtcNow,
                Outputs = new()
                {
                    ["Output"] = [new() { Values = new()
                    {
                        ["Text"] = "First line\nSecond line",
                        ["TimestampText"] = "2026-11-18T16:48:55Z",
                        ["Count"] = 0
                    } }]
                }
            });
            var executions = new ProjectExecutions(storePath);

            executions.Refresh();
            var table = ProjectOutputTable.Create(executions.GetAll().Single().Response!.Outputs["Output"]);

            Assert.AreEqual("First line\nSecond line", table.Rows[0]["Text"]);
            Assert.AreEqual("2026-11-18T16:48:55Z", table.Rows[0]["TimestampText"]);
            Assert.AreEqual("0", table.Rows[0]["Count"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SparseDatasets_KeepEveryFieldAlignedWithItsRow()
    {
        var datasets = new List<ProjectOutputDatasetDto>
        {
            new() { Values = new() { ["Name"] = "First", ["Count"] = 0 } },
            new() { Values = new() { ["Details"] = "Line one\nLine two", ["Name"] = "Second" } }
        };

        var table = ProjectOutputTable.Create(datasets);

        Assert.AreEqual(3, table.Columns.Count);
        Assert.AreEqual("First", table.Rows[0]["Name"]);
        Assert.AreEqual("0", table.Rows[0]["Count"]);
        Assert.AreEqual(DBNull.Value, table.Rows[0]["Details"]);
        Assert.AreEqual("Second", table.Rows[1]["Name"]);
        Assert.AreEqual("Line one\nLine two", table.Rows[1]["Details"]);
        Assert.AreEqual(DBNull.Value, table.Rows[1]["Count"]);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ReloadedRunnerResponse_DisplaysTextWithoutJsonEscapes(bool systemTextJson)
    {
        var response = new RunnerResponse
        {
            Outputs = new()
            {
                ["Answer"] = [new() { Values = new() { ["Text"] = "First line\n\"Second line\"", ["Empty"] = null! } }]
            }
        };
        var json = JsonConvert.SerializeObject(response);
        var reloaded = systemTextJson
            ? System.Text.Json.JsonSerializer.Deserialize<RunnerResponse>(json)!
            : JsonConvert.DeserializeObject<RunnerResponse>(json)!;

        var table = ProjectOutputTable.Create(reloaded.Outputs["Answer"]);

        Assert.AreEqual("First line\n\"Second line\"", table.Rows[0]["Text"]);
        Assert.AreEqual(string.Empty, table.Rows[0]["Empty"]);
        Assert.AreEqual(2, reloaded.Outputs["Answer"][0].Values.Count);
    }
}
