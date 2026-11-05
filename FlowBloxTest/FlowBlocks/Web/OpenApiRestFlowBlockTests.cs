using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlowBloxTest.FlowBlocks.Web
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class OpenApiRestFlowBlockTests : FlowBloxTestsBase
    {
        [TestInitialize]
        public void Initialize()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        }

        [TestMethod]
        public void DefinitionWithUnrelatedSemanticErrors_KeepsUsableEndpointsAndShowsWarnings()
        {
            var block = CreateFlowBlock<OpenApiRestFlowBlock>();
            var definition = Newtonsoft.Json.Linq.JObject.Parse(Definition);
            definition["paths"]!["/participants/{otherId}"] = definition["paths"]!["/participants/{id}"]!.DeepClone();

            block.OpenApiSource = definition.ToString();

            Assert.IsTrue(block.EndpointSuggestions.Any(x => x.Contains("GET /participants/{id}", StringComparison.Ordinal)));
            Assert.AreEqual(string.Empty, block.DefinitionError);
            Assert.IsFalse(string.IsNullOrWhiteSpace(block.DefinitionWarnings));
            block.OpenApiSource = "{invalid JSON";
            Assert.HasCount(0, block.EndpointSuggestions);
            Assert.IsFalse(string.IsNullOrWhiteSpace(block.DefinitionError));
        }

        [TestMethod]
        public void DirectOpenApiDefinition_GeneratesGroupedEndpointsParametersAndPayload()
        {
            var block = CreateFlowBlock<OpenApiRestFlowBlock>();

            block.OpenApiSource = Definition;

            CollectionAssert.AreEqual(
                new[]
                {
                    "Participant | POST /participants - Create participant",
                    "Participant | GET /participants/{id} - Read participant"
                },
                block.EndpointSuggestions.ToArray());

            block.Endpoint = block.EndpointSuggestions[0];

            Assert.AreEqual("https://api.example.test/v1", block.BaseUrl);
            Assert.IsFalse(string.IsNullOrWhiteSpace(block.EndpointDescription));
            Assert.AreEqual("application/json", block.ContentType);
            Assert.AreEqual("X-Tenant", block.HeaderParameters.Single().Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(block.HeaderParameters.Single().Description));
            Assert.AreEqual("demo", block.HeaderParameters.Single().Value);
            Assert.IsTrue(block.Payload.Contains("\"name\"", StringComparison.Ordinal));
            Assert.IsTrue(block.Payload.Contains("\"age\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void ChangingEndpoint_ReplacesGeneratedConfiguration()
        {
            var block = CreateFlowBlock<OpenApiRestFlowBlock>();
            block.OpenApiSource = Definition;
            block.Endpoint = block.EndpointSuggestions[0];
            block.Payload = "custom";
            block.HeaderParameters.Single().Value = "changed";

            block.Endpoint = block.EndpointSuggestions[1];

            Assert.AreEqual(string.Empty, block.Payload);
            Assert.AreEqual(0, block.HeaderParameters.Count);
            Assert.AreEqual("id", block.RequestParameters.Single().Name);
            Assert.AreEqual("Path", block.RequestParameters.Single().Location);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("https://jira.example.test/jira/rest")]
        [DataRow("$Input:JiraBaseUrl/rest")]
        public void ChangingEndpoint_FillsOnlyBlankBaseUrl(string? configuredBaseUrl)
        {
            var block = CreateFlowBlock<OpenApiRestFlowBlock>();
            var definition = Newtonsoft.Json.Linq.JObject.Parse(Definition);
            definition["paths"]!["/participants/{id}"]!["get"]!["servers"] =
                Newtonsoft.Json.Linq.JArray.Parse("""[{"url":"https://other.example.test/v2"}]""");
            block.OpenApiSource = definition.ToString();
            block.Endpoint = block.EndpointSuggestions[0];
            block.BaseUrl = configuredBaseUrl;

            block.Endpoint = block.EndpointSuggestions[1];

            var expectedBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? "https://other.example.test/v2"
                : configuredBaseUrl;
            Assert.AreEqual(expectedBaseUrl, block.BaseUrl);
            Assert.AreEqual("id", block.RequestParameters.Single().Name);

            // Even an automatically filled URL remains set when another endpoint is selected.
            block.Endpoint = block.EndpointSuggestions[0];
            Assert.AreEqual(expectedBaseUrl, block.BaseUrl);
        }

        [TestMethod]
        [DataRow("valid")]
        [DataRow("templatedServer")]
        [DataRow("invalid")]
        [DataRow("empty")]
        [DataRow("unavailable")]
        public void RemoteDefinition_PlainTextJsonAndReentrantSuggestions_LoadOnceAndAllowExplicitRetry(string scenario)
        {
            var definition = Newtonsoft.Json.Linq.JObject.Parse(Definition);
            definition["servers"]![0]!["url"] = "/v1";
            var validDefinition = definition.ToString();
            if (scenario == "empty")
                definition["paths"] = new Newtonsoft.Json.Linq.JObject();
            using var server = new DefinitionServer
            {
                Body = scenario == "invalid" ? "{invalid JSON" : definition.ToString(),
                Status = scenario == "unavailable" ? "503 Service Unavailable" : "200 OK"
            };
            if (scenario == "templatedServer")
            {
                definition["servers"]![0] = new Newtonsoft.Json.Linq.JObject
                {
                    ["url"] = "http://{baseurl}/v1",
                    ["variables"] = new Newtonsoft.Json.Linq.JObject
                    {
                        ["baseurl"] = new Newtonsoft.Json.Linq.JObject { ["default"] = server.BaseUrl[7..] }
                    }
                };
                server.Body = definition.ToString();
            }
            var block = CreateFlowBlock<OpenApiRestFlowBlock>();
            var notifications = 0;
            // Model the property factory asking for choices synchronously when notified.
            // Bound the callback so the regression fails without overflowing the test process.
            block.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(block.EndpointSuggestions) && ++notifications <= 4)
                    block.GetEndpointSuggestions().ToArray();
            };
            block.EndpointSuggestions.CollectionChanged += (_, _) => block.GetEndpointSuggestions().ToArray();

            block.OpenApiSource = server.Source;
            for (var index = 0; index < 3; index++)
                block.GetEndpointSuggestions().ToArray();

            Assert.AreEqual(1, server.RequestCount);
            Assert.AreEqual(1, notifications);
            if (scenario is "invalid" or "unavailable")
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(block.DefinitionError));
                Assert.HasCount(0, block.EndpointSuggestions);
            }
            else
            {
                Assert.AreEqual(string.Empty, block.DefinitionError);
                Assert.HasCount(scenario == "empty" ? 0 : 2, block.EndpointSuggestions);
                if (scenario != "empty")
                {
                    block.Endpoint = block.EndpointSuggestions[0];
                    Assert.AreEqual(server.BaseUrl + "/v1", block.BaseUrl);
                }
            }

            server.Body = validDefinition;
            server.Status = "200 OK";
            block.ReloadDefinition(forceRefresh: true);

            Assert.AreEqual(2, server.RequestCount);
            Assert.AreEqual(2, notifications);
            Assert.AreEqual(string.Empty, block.DefinitionError);
            Assert.HasCount(2, block.EndpointSuggestions);
            block.Endpoint = block.EndpointSuggestions[0];
            Assert.AreEqual(server.BaseUrl + "/v1", block.BaseUrl);
            block.BaseUrl = "https://configured.example.test";
            block.Payload = "custom payload";
            block.GetEndpointSuggestions().ToArray();
            Assert.AreEqual("https://configured.example.test", block.BaseUrl);
            Assert.AreEqual("custom payload", block.Payload);

            var selectedEndpoint = block.Endpoint;
            block.HeaderParameters.Single().Value = "custom tenant";
            block.ReloadDefinition(forceRefresh: true, preserveSelection: true);
            Assert.AreEqual(3, server.RequestCount);
            Assert.AreEqual(selectedEndpoint, block.Endpoint);
            Assert.AreEqual("https://configured.example.test", block.BaseUrl);
            Assert.AreEqual("custom payload", block.Payload);
            Assert.AreEqual("custom tenant", block.HeaderParameters.Single().Value);

            var secondBlock = CreateFlowBlock<OpenApiRestFlowBlock>();
            secondBlock.OpenApiSource = server.Source;
            Assert.HasCount(2, secondBlock.EndpointSuggestions);
            Assert.AreEqual(3, server.RequestCount, "Successful definitions should still use the shared URL cache.");

            server.Status = "503 Service Unavailable";
            Assert.ThrowsExactly<HttpRequestException>(() => block.ReloadDefinition(forceRefresh: true, preserveSelection: true));
            Assert.IsFalse(string.IsNullOrWhiteSpace(block.DefinitionError));
            block.GetEndpointSuggestions().ToArray();
            Assert.AreEqual(4, server.RequestCount);
            Assert.AreEqual(selectedEndpoint, block.Endpoint);
            Assert.AreEqual("custom payload", block.Payload);
        }

        private sealed class DefinitionServer : IDisposable
        {
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
            private readonly Task _serving;
            private int _requestCount;

            public string Body { get; set; } = string.Empty;
            public string Status { get; set; } = "200 OK";
            public int RequestCount => Volatile.Read(ref _requestCount);
            public string BaseUrl { get; }
            public string Source => BaseUrl + "/openapi.json";

            public DefinitionServer()
            {
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
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                        Interlocked.Increment(ref _requestCount);
                        var body = Body;
                        var response = Encoding.UTF8.GetBytes(
                            $"HTTP/1.1 {Status}\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                        await stream.WriteAsync(response, _stop.Token);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            }

            public void Dispose()
            {
                _stop.Cancel();
                _serving.GetAwaiter().GetResult();
                _listener.Stop();
                _stop.Dispose();
            }
        }

        private const string Definition = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Participants", "version": "1.0" },
              "servers": [{ "url": "https://api.example.test/v1" }],
              "paths": {
                "/participants": {
                  "post": {
                    "tags": ["Participant"],
                    "summary": "Create participant",
                    "parameters": [{
                      "name": "X-Tenant",
                      "in": "header",
                      "required": true,
                      "description": "Tenant identifier",
                      "schema": { "type": "string", "default": "demo" }
                    }],
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "properties": {
                              "name": { "type": "string" },
                              "age": { "type": "integer" }
                            }
                          }
                        }
                      }
                    },
                    "responses": { "200": { "description": "OK" } }
                  }
                },
                "/participants/{id}": {
                  "get": {
                    "tags": ["Participant"],
                    "summary": "Read participant",
                    "parameters": [{
                      "name": "id",
                      "in": "path",
                      "required": true,
                      "description": "Participant identifier",
                      "schema": { "type": "string" }
                    }],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
    }
}
