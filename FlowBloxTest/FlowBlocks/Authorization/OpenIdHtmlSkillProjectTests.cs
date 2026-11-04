using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Test.Runtime;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlowBloxTest.FlowBlocks.Authorization;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class OpenIdHtmlSkillProjectTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PasswordGrant_ResolvesProtectedInputsAndFetchesHtmlOnlyAfterAuthentication(bool rejectToken)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var requests = new List<(string Path, Dictionary<string, string> Headers, string Body)>();
        const string html = "<!doctype html><html><body>Geschützter Inhalt</body></html>";
        var server = ServeRequests();

        try
        {
            var main = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = main;
            var password = main.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "LoginPassword");
            password.IsPassword = true;
            password.StringValue = "p&a+ss=word";
            var secret = main.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "ClientSecret");
            secret.IsPassword = true;
            secret.StringValue = "client&secret";

            var path = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "FetchHtmlWithOpenId.fbprj");
            Assert.IsNotNull(path);
            var skill = FlowBloxProject.FromFile(path);
            var inputs = ProjectExecutionToolUtilities.ResolveInputParameters(new JObject
            {
                ["TokenEndpoint"] = endpoint + "/token",
                ["ClientId"] = "html-client",
                ["ClientSecret"] = secret.FullyQualifiedName,
                ["UserName"] = "user@example.test",
                ["Password"] = password.FullyQualifiedName,
                ["Scope"] = "openid html.read",
                ["Audience"] = "html-api",
                ["Url"] = endpoint + "/page"
            }, skill);

            FlowBloxProjectManager.Instance.ActiveProject = skill;
            foreach (var input in inputs)
                skill.UserFields.Single(field => field.Name == input.Key).StringValue = input.Value;

            var runtime = new FlowBloxUnitTestRuntime(skill);
            runtime.LogMessageCreated += (_, message, _) => Console.WriteLine(message);
            runtime.Execute();
            await server;

            Assert.AreEqual("/token", requests[0].Path);
            var form = requests[0].Body.Split('&')
                .Select(part => part.Split('=', 2))
                .ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]));
            Assert.AreEqual("password", form["grant_type"]);
            Assert.AreEqual("html-client", form["client_id"]);
            Assert.AreEqual(secret.StringValue, form["client_secret"]);
            Assert.AreEqual("user@example.test", form["username"]);
            Assert.AreEqual(password.StringValue, form["password"]);
            Assert.AreEqual("openid html.read", form["scope"]);
            Assert.AreEqual("html-api", form["audience"]);

            var outputs = runtime.GetOutputDatasets("Output");
            if (rejectToken)
            {
                Assert.AreEqual(0, outputs.Count);
                Assert.AreEqual(1, requests.Count);
                Assert.IsFalse(listener.Pending(), "The HTML endpoint must not be called after authentication fails.");
            }
            else
            {
                Assert.AreEqual("/page", requests[1].Path);
                Assert.AreEqual("Bearer html-access-token", requests[1].Headers["Authorization"]);
                Assert.AreEqual("text/html", requests[1].Headers["Accept"]);
                Assert.AreEqual(1, outputs.Count);
                Assert.AreEqual(1, outputs[0].Values.Count);
                Assert.AreEqual(html, outputs[0].Values["Content"]);
            }
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }

        async Task ServeRequests()
        {
            for (var index = 0; index < (rejectToken ? 1 : 2); index++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(timeout.Token);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeout.Token)))
                {
                    var parts = line.Split(':', 2);
                    headers[parts[0]] = parts[1].Trim();
                }

                var length = headers.TryGetValue("Content-Length", out var value) ? int.Parse(value) : 0;
                var body = new char[length];
                var read = 0;
                while (read < length)
                {
                    var count = await reader.ReadAsync(body.AsMemory(read), timeout.Token);
                    if (count == 0)
                        throw new EndOfStreamException("Incomplete token request body.");
                    read += count;
                }
                requests.Add((requestLine!.Split(' ')[1], headers, new string(body)));

                var payload = index == 0
                    ? rejectToken ? "{\"error\":\"invalid_grant\"}" : "{\"access_token\":\"html-access-token\",\"token_type\":\"Bearer\"}"
                    : html;
                var status = rejectToken ? "400 Bad Request" : "200 OK";
                var contentType = index == 0 ? "application/json" : "text/html; charset=utf-8";
                var response = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {Encoding.UTF8.GetByteCount(payload)}\r\nConnection: close\r\n\r\n{payload}");
                await stream.WriteAsync(response, timeout.Token);
            }
        }
    }
}
