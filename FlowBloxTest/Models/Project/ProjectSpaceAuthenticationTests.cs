using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow.ExecuteProject;
using FlowBlox.Core.Util;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class ProjectSpaceAuthenticationTests
{
    [TestMethod]
    [DataRow(401, "", true)]
    [DataRow(403, "", true)]
    [DataRow(404, "", true)]
    [DataRow(200, "", true)]
    [DataRow(500, "", false)]
    [DataRow(404, "existing-session", false)]
    public async Task InaccessibleProject_RequestsLoginOnlyForAnonymousAccessFailures(int status, string token, bool loginRequired)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var body = status == 200 ? "null" : "{\"error\":\"Project unavailable\"}";
        var serve = SendResponseAsync(listener, status, body);
        Exception? failure = null;
        try
        {
            await FlowBloxProject.FromProjectSpaceGuidAsync(Guid.NewGuid().ToString(), token,
                new FlowBloxWebApiService(endpoint)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await serve;
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual(loginRequired, failure is ProjectSpaceLoginRequiredException);
    }

    [TestMethod]
    public async Task InaccessibleContent_RequestsLoginForAnonymousAccessFailure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var guid = Guid.NewGuid().ToString();
        var serve = SendContentResponsesAsync(listener, guid, 403, "{\"error\":\"Login required\"}");
        await Assert.ThrowsExactlyAsync<ProjectSpaceLoginRequiredException>(() =>
            FlowBloxProject.FromProjectSpaceGuidAsync(guid, "", new FlowBloxWebApiService(endpoint))
                .WaitAsync(TimeSpan.FromSeconds(5)));
        await serve;
    }

    [TestMethod]
    public async Task PublicProject_LoadsWithoutCredentials()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var guid = Guid.NewGuid().ToString();
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("project_file.json").Open());
            writer.Write("{\"ProjectName\":\"Public project\",\"UserFields\":[],\"FlowBlocks\":[],\"ManagedObjects\":[]}");
        }
        var body = JsonConvert.SerializeObject(new { Content = Convert.ToBase64String(memory.ToArray()) });
        var serve = SendContentResponsesAsync(listener, guid, 200, body);
        var project = await FlowBloxProject.FromProjectSpaceGuidAsync(guid, "", new FlowBloxWebApiService(endpoint))
            .WaitAsync(TimeSpan.FromSeconds(5));
        await serve;
        Assert.AreEqual("Public project", project.ProjectName);
        Assert.AreEqual(guid, project.ProjectSpaceGuid);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ExecuteProjectInputs_LoadPrivateProjectWithAccountTokenAndExcludeMemoryFields()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var guid = Guid.NewGuid().ToString("N");
        const string token = "private-project-token";
        var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl);
        var previousEndpoint = option.Value;
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("project_file.json").Open()))
        {
            writer.Write("""
                {"ProjectName":"Private project","FlowBlocks":[],"ManagedObjects":[],"UserFields":[
                  {"Name":"Question","UserField":true,"UserFieldType":"Input","DefaultValue":"Default question"},
                  {"Name":"Password","UserField":true,"UserFieldType":"Input","IsPassword":true},
                  {"Name":"Internal","UserField":true,"UserFieldType":"Memory","StringValue":"Private memory"}]}
                """);
        }
        var body = JsonConvert.SerializeObject(new { Content = Convert.ToBase64String(memory.ToArray()) });
        var serve = SendContentResponsesAsync(listener, guid, 200, body, token);
        try
        {
            option.Value = endpoint;
            FlowBloxAccountManager.Instance.SetSession(endpoint, new FbUserData(), token);
            var block = new ExecuteProjectFlowBlock { ProjectSpaceGuid = guid };
            var mapping = new ExecuteProjectParameterMappingEntry();
            block.ParameterMappings.Add(mapping);

            CollectionAssert.AreEqual(new[] { "Password", "Question" }, mapping.GetTargetKeys().ToArray());
            Assert.AreEqual("Default question", block.InputFields.Single(field => field.Name == "Question").DefaultValue);
            Assert.IsTrue(block.InputFields.Single(field => field.Name == "Password").IsPassword);
            await serve.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            FlowBloxAccountManager.Instance.SetSession(endpoint, null, null);
            option.Value = previousEndpoint;
        }
    }

    private static async Task SendContentResponsesAsync(TcpListener listener, string guid, int status, string body, string? expectedToken = null)
    {
        await SendResponseAsync(listener, 200, JsonConvert.SerializeObject(new { Guid = guid, Name = "Public project" }), expectedToken);
        await SendResponseAsync(listener, status, body, expectedToken);
    }

    private static async Task SendResponseAsync(TcpListener listener, int status, string body, string? expectedToken = null)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var requestHeaders = new List<string>();
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            requestHeaders.Add(line);
        var bytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(bytes);
        if (expectedToken != null)
            CollectionAssert.Contains(requestHeaders, "Authorization: Bearer " + expectedToken);
    }
}
