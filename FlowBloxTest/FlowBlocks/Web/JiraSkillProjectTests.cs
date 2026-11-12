using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Test.Runtime;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.FlowBlocks.Web;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class JiraSkillProjectTests
{
    [TestMethod]
    [DataRow("search")]
    [DataRow("nextPage")]
    [DataRow("emptySearch")]
    [DataRow("details")]
    [DataRow("comments")]
    [DataRow("emptyComments")]
    [DataRow("optionalMissing")]
    [DataRow("unauthorized")]
    [DataRow("notFound")]
    [DataRow("commentsFailedSecondPage")]
    [DataRow("invalidLevel")]
    [DataRow("missingLevel")]
    [DataRow("missingPassword")]
    public void BundledJiraSkill_ExecutesItsGraphAndReturnsScopedStructuredOutputs(string scenario)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        using var api = new LocalJiraApi(scenario);
        try
        {
            var search = scenario is "search" or "nextPage" or "emptySearch" or "unauthorized";
            var file = DistributedDataCatalog.ResolvePath("skill_project", search ? "SearchJiraTickets.fbprj" : "GetJiraTicket.fbprj");
            Assert.IsNotNull(file);
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var source in json.SelectTokens("$..OpenApiSource").ToList())
                source.Replace(Environment.GetEnvironmentVariable("FLOWBLOX_TEST_JIRA_OPENAPI") ?? Definition);
            var project = FlowBloxProject.FromJsonContents(json.ToString(), null);
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var withComments = scenario is "comments" or "emptyComments" or "commentsFailedSecondPage";
            const string password = "secret:&+Jira";
            const string jql = "project = DEMO AND summary ~ \"A&B\" ORDER BY key ASC";
            var values = new Dictionary<string, string>
            {
                ["JiraBaseUrl"] = api.BaseUrl + "/jira",
                ["Username"] = "jira-user",
                ["Password"] = scenario == "missingPassword" ? "" : password,
                ["Jql"] = jql,
                ["TicketKey"] = "DEMO-1",
                ["StartAt"] = scenario == "nextPage" ? "2" : "0",
                ["DetailLevel"] = scenario == "invalidLevel" ? "UnknownLevel" : scenario == "missingLevel" ? ""
                    : withComments ? "DescriptionMetadataAndComments" : "DescriptionAndMetadata"
            };
            foreach (var field in project.UserFields)
            {
                if (values.TryGetValue(field.Name, out var value))
                    field.StringValue = value;
            }
            var logs = new List<string>();
            using var runtime = new FlowBloxUnitTestRuntime(project);
            runtime.LogMessageCreated += (_, message, _) => logs.Add(message);
            if (scenario is "invalidLevel" or "missingLevel" or "missingPassword")
            {
                Assert.ThrowsExactly<ValidationException>(runtime.Execute);
                Assert.HasCount(0, api.Requests);
                return;
            }

            runtime.Execute();

            var diagnostic = string.Join(Environment.NewLine, logs) + Environment.NewLine
                + string.Join(Environment.NewLine, project.FlowBlocks.OfType<OpenApiRestFlowBlock>().Select(x => x.DefinitionError));
            Assert.IsTrue(api.Requests.Count > 0, diagnostic);
            Assert.IsFalse(logs.Any(x => x.Contains(password, StringComparison.Ordinal)));
            var authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("jira-user:" + password));
            Assert.IsTrue(api.Requests.All(x => x.Authorization == authorization));
            Assert.IsTrue(api.Requests.All(x => x.Path.StartsWith("/jira/rest/api/2/", StringComparison.Ordinal)));
            var output = runtime.GetOutputDatasets("Output");
            var comments = runtime.GetOutputDatasets("Comments");
            if (scenario is "unauthorized" or "notFound" or "commentsFailedSecondPage")
            {
                Assert.IsTrue(runtime.Aborted, diagnostic);
                Assert.HasCount(0, output);
                Assert.HasCount(0, comments);
                return;
            }
            Assert.IsFalse(runtime.Aborted, diagnostic);
            if (search)
            {
                Assert.HasCount(1, api.Requests);
                var request = api.Requests.Single();
                Assert.AreEqual(jql, request.Jql);
                Assert.AreEqual("50", request.PageSize);
                Assert.IsFalse(request.Fields.Contains("description", StringComparison.Ordinal));
                Assert.IsFalse(request.Fields.Contains("comment", StringComparison.Ordinal));
                Assert.IsFalse(request.Fields.Contains("*all", StringComparison.Ordinal));
                Assert.HasCount(scenario == "emptySearch" ? 0 : scenario == "nextPage" ? 1 : 2, output);
                Assert.IsTrue(output.All(x => !x.Values.ContainsKey("Description") && !x.Values.ContainsKey("Body")));
                if (output.Count > 0)
                {
                    Assert.AreEqual(scenario == "nextPage" ? "DEMO-3" : "DEMO-1", output[0].Values["TicketKey"]);
                    Assert.AreEqual("Blocked", output[0].Values["Status"]);
                    Assert.AreEqual("Alice", output[0].Values["Creator"]);
                    Assert.AreEqual("2026-11-05T10:15:00.000+0100", output[0].Values["Updated"]);
                    Assert.AreEqual("", output[0].Values["Assignee"]);
                }
                var page = runtime.GetOutputDatasets("Page").Single();
                Assert.AreEqual(scenario == "emptySearch" ? 0 : 3, Convert.ToInt32(page.Values["Total"]));
                Assert.AreEqual(scenario == "nextPage" ? 2 : 0, Convert.ToInt32(page.Values["StartAt"]));
                Assert.AreEqual(2, Convert.ToInt32(page.Values["PageSize"]));
                return;
            }

            Assert.HasCount(1, output, diagnostic);
            var ticket = output.Single().Values;
            Assert.AreEqual("DEMO-1", ticket["TicketKey"]);
            Assert.AreEqual("Blocked", ticket["Status"]);
            Assert.AreEqual(scenario == "optionalMissing" ? "" : "Description\nwith wiki *markup*", ticket["Description"]);
            if (scenario == "optionalMissing")
            {
                Assert.IsNull(ticket["StoryPoints"]);
                Assert.AreEqual("", ticket["Sprint"]);
            }
            else
            {
                Assert.AreEqual(5d, Convert.ToDouble(ticket["StoryPoints"]));
                Assert.AreEqual("1.2500.0.0", ticket["Sprint"]);
            }
            var commentRequests = api.Requests.Where(x => x.Path.Contains("/comment?", StringComparison.Ordinal)).ToList();
            Assert.HasCount(withComments ? scenario == "emptyComments" ? 1 : 2 : 0, commentRequests);
            if (!withComments || scenario == "emptyComments")
            {
                Assert.HasCount(0, comments);
                return;
            }
            CollectionAssert.AreEqual(new[] { "0", "2" }, commentRequests.Select(x => x.Offset).ToArray());
            Assert.HasCount(3, comments);
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, comments.Select(x => x.Values["CommentId"]!.ToString()).ToArray());
            Assert.IsTrue(comments.All(x => (string)x.Values["TicketKey"] == "DEMO-1"));
            Assert.AreEqual("First\ncomment & details", comments[0].Values["Body"]);
            Assert.AreEqual("2026-11-04T09:00:00.000+0100", comments[0].Values["Created"]);
            Assert.AreEqual("", comments[1].Values["Author"]);
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    private const string Definition = """
        {"openapi":"3.0.1","info":{"title":"Jira skill fixture","version":"2"},"paths":{
        "/api/2/search":{"get":{"tags":["search"],"summary":"Get issues using JQL","responses":{"200":{"description":"OK"}}}},
        "/api/2/field":{"get":{"tags":["field"],"summary":"Get all fields, both System and Custom","responses":{"200":{"description":"OK"}}}},
        "/api/2/issue/{issueIdOrKey}":{"get":{"tags":["issue"],"summary":"Get issue for key","parameters":[{"name":"issueIdOrKey","in":"path","required":true,"schema":{"type":"string"}}],"responses":{"200":{"description":"OK"}}}},
        "/api/2/issue/{issueIdOrKey}/comment":{"get":{"tags":["issue"],"summary":"Get comments for an issue","parameters":[{"name":"issueIdOrKey","in":"path","required":true,"schema":{"type":"string"}}],"responses":{"200":{"description":"OK"}}}}}}
        """;

    private sealed class LocalJiraApi : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
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
                    Requests.Add(new Request(path, authorization, query["jql"] ?? "", query["fields"] ?? "", query["startAt"] ?? "", query["maxResults"] ?? ""));
                    var status = _scenario == "unauthorized" ? "401 Unauthorized"
                        : _scenario == "notFound" && path.Contains("/issue/", StringComparison.Ordinal) ? "404 Not Found"
                        : _scenario == "commentsFailedSecondPage" && path.Contains("/comment?", StringComparison.Ordinal) && query["startAt"] == "2" ? "500 Internal Server Error" : "200 OK";
                    var body = status == "200 OK" ? CreateResponse(path, query["startAt"] ?? "0") : "{\"errorMessages\":[\"Access failed\"]}";
                    var bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                    await stream.WriteAsync(bytes, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        private string CreateResponse(string path, string offset)
        {
            if (path.Contains("/field", StringComparison.Ordinal))
                return _scenario == "optionalMissing" ? "[]" : """
                    [{"id":"customfield_73001","name":"Story-Punkte"},
                     {"id":"customfield_73002","name":"Sprint","schema":{"custom":"com.pyxis.greenhopper.jira:gh-sprint"}}]
                    """;
            if (path.Contains("/search?", StringComparison.Ordinal))
            {
                var issues = _scenario == "emptySearch" ? new JArray() : offset == "2" ? new JArray(Issue("DEMO-3"))
                    : new JArray(Issue("DEMO-1"), Issue("DEMO-2"));
                return new JObject { ["startAt"] = int.Parse(offset), ["maxResults"] = 2, ["total"] = _scenario == "emptySearch" ? 0 : 3, ["issues"] = issues }.ToString();
            }
            if (path.Contains("/comment?", StringComparison.Ordinal))
            {
                var comments = _scenario == "emptyComments" ? new JArray() : offset == "2" ? new JArray(Comment("3", "Carol"))
                    : new JArray(Comment("1", "Alice"), Comment("2", null));
                return new JObject { ["startAt"] = int.Parse(offset), ["maxResults"] = 2, ["total"] = _scenario == "emptyComments" ? 0 : 3, ["comments"] = comments }.ToString();
            }
            return Issue("DEMO-1").ToString();
        }

        private JObject Issue(string key) => new()
        {
            ["key"] = key,
            ["fields"] = new JObject
            {
                ["summary"] = "Ticket " + key,
                ["project"] = new JObject { ["key"] = "DEMO" },
                ["issuetype"] = new JObject { ["name"] = "Bug" },
                ["status"] = new JObject { ["name"] = "Blocked", ["statusCategory"] = new JObject { ["key"] = "indeterminate" } },
                ["creator"] = new JObject { ["displayName"] = "Alice" },
                ["assignee"] = null,
                ["priority"] = null,
                ["created"] = "2026-11-01T09:00:00.000+0100",
                ["updated"] = "2026-11-05T10:15:00.000+0100",
                ["description"] = _scenario == "optionalMissing" ? null : "Description\nwith wiki *markup*",
                ["customfield_73001"] = _scenario == "optionalMissing" ? null : 5,
                ["customfield_73002"] = _scenario == "optionalMissing" ? null : new JArray(new JObject { ["name"] = "1.2500.0.0" })
            }
        };

        private static JObject Comment(string id, string? author) => new()
        {
            ["id"] = id,
            ["author"] = author == null ? null : new JObject { ["displayName"] = author, ["avatarUrls"] = new JObject { ["48x48"] = "https://avatars.example.test/image" } },
            ["created"] = "2026-11-04T09:00:00.000+0100",
            ["updated"] = "2026-11-05T09:00:00.000+0100",
            ["body"] = id == "1" ? "First\ncomment & details" : "Comment " + id
        };

        public void Dispose()
        {
            _stop.Cancel();
            _serving.GetAwaiter().GetResult();
            _listener.Stop();
            _stop.Dispose();
        }
    }

    private sealed record Request(string Path, string Authorization, string Jql, string Fields, string Offset, string PageSize);
}
