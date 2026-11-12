using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Test.Runtime;
using Newtonsoft.Json.Linq;
using OfficeOpenXml;

namespace FlowBloxTest.FlowBlocks.Web;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class JiraDataCenterExportExampleTests
{
    [TestMethod]
    [DataRow("success")]
    [DataRow("trailingSlash")]
    [DataRow("empty")]
    [DataRow("unauthorized")]
    [DataRow("failedSecondPage")]
    [DataRow("pageLimit")]
    public void Example_ExportsCompleteAlignedWorkbookOrPreservesPreviousFileOnFailure(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxJiraTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        using var api = new LocalJiraApi(scenario);
        var output = Path.Combine(directory, "Tickets.xlsx");
        var previousContent = Encoding.UTF8.GetBytes("Previous export");
        File.WriteAllBytes(output, previousContent);

        try
        {
            var file = DistributedDataCatalog.ResolvePath("example_project", "Jira-Data-Center-JQL-to-XLSX.fbprj");
            Assert.IsNotNull(file);
            var json = JObject.Parse(File.ReadAllText(file));
            // Replace only the public definition source; the example's actual graph and mappings are exercised.
            foreach (var source in json.SelectTokens("$..OpenApiSource").ToList())
                source.Replace(Environment.GetEnvironmentVariable("FLOWBLOX_TEST_JIRA_OPENAPI") ?? Definition);
            var project = FlowBloxProject.FromJsonContents(json.ToString(), null);
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var values = new Dictionary<string, string>
            {
                ["JiraBaseUrl"] = api.BaseUrl + "/jira" + (scenario == "trailingSlash" ? "/" : string.Empty),
                ["ProjectKey"] = "DEMO",
                ["Username"] = "export-user",
                ["Password"] = "p&ss:+üword",
                ["OutputFile"] = output
            };
            foreach (var field in project.UserFields)
            {
                if (values.TryGetValue(field.Name, out var value))
                    field.StringValue = value;
            }
            if (scenario == "pageLimit")
                project.FlowBlocks.OfType<OpenApiRestFlowBlock>().Single(x => x.Paginate).MaximumPages = 1;
            var logs = new List<string>();
            var runtime = new FlowBloxUnitTestRuntime(project);
            runtime.LogMessageCreated += (_, message, _) => logs.Add(message);

            runtime.Execute();

            Assert.IsFalse(logs.Any(x => x.Contains(values["Password"], StringComparison.Ordinal)));
            var expectedAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("export-user:" + values["Password"]));
            Assert.IsTrue(api.Requests.Count > 0, string.Join(Environment.NewLine, logs)
                + string.Join(Environment.NewLine, project.FlowBlocks.OfType<OpenApiRestFlowBlock>().Select(x => x.DefinitionError)));
            Assert.IsTrue(api.Requests.All(x => x.Authorization == expectedAuthorization));
            Assert.IsTrue(api.Requests.All(x => x.Path.StartsWith("/jira/rest/api/2/", StringComparison.Ordinal)));

            if (scenario is "unauthorized" or "failedSecondPage" or "pageLimit")
            {
                Assert.IsTrue(runtime.Aborted, string.Join(Environment.NewLine, logs));
                var expectedRequests = scenario == "unauthorized" ? 1 : scenario == "pageLimit" ? 2 : 3;
                Assert.HasCount(expectedRequests, api.Requests);
                if (scenario == "failedSecondPage")
                    Assert.AreEqual("2", api.Requests.Last().Offset);
                CollectionAssert.AreEqual(previousContent, File.ReadAllBytes(output));
                return;
            }

            Assert.IsFalse(runtime.Aborted, string.Join(Environment.NewLine, logs));
            using var workbook = new ExcelPackage(new FileInfo(output));
            var sheet = workbook.Workbook.Worksheets.Single();
            CollectionAssert.AreEqual(
                new[] { "Ticket key", "Ticket type", "Ticket name", "Sprint", "Creator", "Assignee", "Story points", "Description" },
                Enumerable.Range(1, 8).Select(column => sheet.Cells[1, column].Text).ToArray());
            if (scenario == "empty")
            {
                Assert.AreEqual(1, sheet.Dimension.End.Row);
                return;
            }

            Assert.AreEqual(4, sheet.Dimension.End.Row);
            CollectionAssert.AreEqual(new[] { "DEMO-1", "DEMO-2", "DEMO-3" },
                Enumerable.Range(2, 3).Select(row => sheet.Cells[row, 1].Text).ToArray());
            Assert.AreEqual("1.2500.0.0", sheet.Cells[2, 4].Text);
            Assert.AreEqual("1.2500.0.1", sheet.Cells[3, 4].Text);
            Assert.AreEqual("Alice", sheet.Cells[2, 5].Text);
            Assert.AreEqual("", sheet.Cells[2, 6].Text);
            Assert.AreEqual("Bob", sheet.Cells[3, 6].Text);
            Assert.AreEqual("5", sheet.Cells[2, 7].Text);
            Assert.IsInstanceOfType<double>(sheet.Cells[2, 7].Value);
            Assert.AreEqual("", sheet.Cells[3, 7].Text);
            Assert.AreEqual("Line one\nBeschreibung mit Umlaut ü", sheet.Cells[2, 8].Text);
            Assert.AreEqual("=SUM(A1:A2)", sheet.Cells[3, 3].Text);
            Assert.AreEqual("", sheet.Cells[3, 3].Formula);
            var searches = api.Requests.Where(x => x.Path.Contains("/search?", StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(new[] { "0", "2" }, searches.Select(x => x.Offset).ToArray());
            Assert.IsTrue(searches.All(x => x.Jql.Contains("project = \"DEMO\"", StringComparison.Ordinal)));
            Assert.IsTrue(searches.All(x => x.Jql.Contains("issuetype in (Bug, Task, Story)", StringComparison.Ordinal)));
            Assert.IsTrue(searches.All(x => x.Jql.Contains("openSprints()", StringComparison.Ordinal)));
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string Definition = """
        {"openapi":"3.0.1","info":{"title":"Jira Data Center test fixture","version":"2"},
        "servers":[{"url":"http://localhost/rest"}],"paths":{
        "/api/2/field":{"get":{"tags":["field"],"summary":"Get all fields, both System and Custom","responses":{"200":{"description":"OK"}}}},
        "/api/2/search":{"get":{"tags":["search"],"summary":"Get issues using JQL","parameters":[
        {"name":"jql","in":"query","schema":{"type":"string"}},
        {"name":"fields","in":"query","schema":{"type":"string"}},
        {"name":"startAt","in":"query","schema":{"type":"integer"}},
        {"name":"maxResults","in":"query","schema":{"type":"integer"}}],"responses":{"200":{"description":"OK"}}}}}}
        """;

    private sealed class LocalJiraApi : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(20));
        private readonly Task _serving;
        private readonly string _scenario;
        public List<Request> Requests { get; } = [];
        public string BaseUrl { get; }

        public LocalJiraApi(string scenario)
        {
            _scenario = scenario;
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _serving = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    var line = await reader.ReadLineAsync(_stop.Token);
                    var path = line!.Split(' ')[1];
                    var authorization = "";
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                    {
                        if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                            authorization = line[14..].Trim();
                    }
                    var query = HttpUtility.ParseQueryString(new Uri(BaseUrl + path).Query);
                    var offset = query["startAt"] ?? "";
                    Requests.Add(new Request(path, authorization, offset, query["jql"] ?? ""));
                    var failed = _scenario == "unauthorized" || _scenario == "failedSecondPage" && offset == "2";
                    var body = failed ? "{\"errorMessages\":[\"Access denied\"]}" : CreateResponse(path, offset);
                    var status = failed ? "401 Unauthorized" : "200 OK";
                    var bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                    await stream.WriteAsync(bytes, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        private string CreateResponse(string path, string offset)
        {
            if (path.EndsWith("/field", StringComparison.Ordinal))
                return """
                    [{"id":"customfield_90001","name":"Sprint","schema":{"custom":"com.pyxis.greenhopper.jira:gh-sprint"}},
                     {"id":"customfield_90002","name":"Story Points","schema":{"custom":"float"}}]
                    """;
            var issues = _scenario == "empty" ? new JArray() : offset == "0"
                ? new JArray(
                    Issue("DEMO-1", "First issue", null, 5, new JArray("Sprint@abc[id=1,state=ACTIVE,name=1.2500.0.0,startDate=2026-01-01]")),
                    Issue("DEMO-2", "=SUM(A1:A2)", "Bob", null, new JArray(new JObject { ["name"] = "1.2500.0.1" })))
                : new JArray(Issue("DEMO-3", "Third issue", "Charlie", 3, null));
            return new JObject
            {
                ["startAt"] = offset == "" ? 0 : int.Parse(offset),
                ["maxResults"] = 2,
                ["total"] = _scenario == "empty" ? 0 : 3,
                ["issues"] = issues
            }.ToString();
        }

        private static JObject Issue(string key, string title, string? assignee, int? points, JArray? sprints) => new()
        {
            ["key"] = key,
            ["fields"] = new JObject
            {
                ["summary"] = title,
                ["issuetype"] = new JObject { ["name"] = "Story" },
                ["creator"] = new JObject { ["displayName"] = "Alice" },
                ["assignee"] = assignee == null ? null : new JObject { ["displayName"] = assignee },
                ["customfield_90001"] = sprints,
                ["customfield_90002"] = points,
                ["description"] = key == "DEMO-1" ? "Line one\nBeschreibung mit Umlaut ü" : null
            }
        };

        public void Dispose()
        {
            _stop.Cancel();
            _serving.GetAwaiter().GetResult();
            _listener.Stop();
            _stop.Dispose();
        }
    }

    private sealed record Request(string Path, string Authorization, string Offset, string Jql);
}
