using System.IO.Compression;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Json;
using FlowBloxTest.FlowBlocks.Knowledge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectSpaceToolTests
{
    private static readonly string ProjectGuid = Guid.NewGuid().ToString("N");
    private string _previousEndpoint = null!;
    private string _previousAppDataDirectory = null!;
    private string _directory = null!;
    private OptionElement _previousLoginOption = null!;
    private FlowBloxProject? _previousProject;

    [TestInitialize]
    public void Initialize()
    {
        var options = FlowBloxOptions.GetOptionInstance();
        var appData = options.GetOption(OptionsConstants.PathsLocalAppDataDir);
        _previousAppDataDirectory = appData.PersistentValue;
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxProjectSpaceToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        appData.PersistentValue = _directory;
        _previousEndpoint = options.GetOption(OptionsConstants.ApiProjectServiceBaseUrl).Value;
        _previousLoginOption = options.GetOption(OptionsConstants.AccountLoginData);
        options.OptionCollection[OptionsConstants.AccountLoginData] = new OptionElement
        {
            Name = OptionsConstants.AccountLoginData,
            Type = OptionElement.OptionType.Password
        };
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
    }

    [TestCleanup]
    public void Cleanup()
    {
        var options = FlowBloxOptions.GetOptionInstance();
        options.GetOption(OptionsConstants.ApiProjectServiceBaseUrl).Value = _previousEndpoint;
        options.OptionCollection[OptionsConstants.AccountLoginData] = _previousLoginOption;
        options.GetOption(OptionsConstants.PathsLocalAppDataDir).PersistentValue = _previousAppDataDirectory;
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task Search_MatchesDescriptionsOrNamesWithoutDownloadingContent()
    {
        using var server = new KnowledgeHttpServer(_ => new KnowledgeHttpResponse
        {
            Body = JsonConvert.SerializeObject(new object[]
            {
                new { Guid = ProjectGuid, Name = "Reusable alpha", Description = "Delta test function", Visibility = "Public", Versions = new[] { new { VersionNumber = 2, HasContent = true } } },
                new { Guid = Guid.NewGuid().ToString("N"), Name = "Reusable beta", Description = "Another test function", Visibility = "Public", Versions = Array.Empty<object>() },
                new { Guid = Guid.NewGuid().ToString("N"), Name = "Private delta", Description = "Private project", Visibility = "Private", Versions = Array.Empty<object>() }
            })
        });
        SetEndpoint(server);

        var response = await ExecuteAsync("GetPSProjects", new JObject { ["searchForNames"] = "DELTA, reusable beta" });

        Assert.IsTrue(response.Ok, response.Error);
        var projects = (JArray)response.Result["projects"]!;
        Assert.HasCount(2, projects);
        Assert.AreEqual(2, projects.Single(project => project.Value<string>("projectSpaceGuid") == ProjectGuid).Value<int>("projectSpaceVersion"));
        Assert.HasCount(1, server.Requests);
        StringAssert.StartsWith(server.Requests[0].Path, "/projects.php?");
        Assert.IsFalse(server.Requests[0].Headers.ContainsKey("Authorization"));
    }

    [TestMethod]
    public async Task Search_FunctionFilter_UsesMetadataOnlyAndReturnsClassificationAndUsage()
    {
        using var server = new KnowledgeHttpServer(_ => new KnowledgeHttpResponse
        {
            Body = JsonConvert.SerializeObject(new[]
            {
                new { Guid = ProjectGuid, Name = "Reusable helper", Description = "Shared function", Visibility = "Public", Classification = "Function", UsedByCount = 3, Versions = Array.Empty<object>() }
            })
        });
        SetEndpoint(server);

        var response = await ExecuteAsync("GetPSProjects", new JObject { ["classification"] = "Function" });

        Assert.IsTrue(response.Ok, response.Error);
        StringAssert.Contains(server.Requests.Single().Path, "classification=Function");
        var project = response.Result["projects"]!.Single()!;
        Assert.AreEqual("Function", project.Value<string>("classification"));
        Assert.AreEqual(3, project.Value<int>("usedByCount"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Inspect_PrefersStableContentAndFallsBackToCurrent(bool stable)
    {
        using var server = CreateProjectServer(stable);
        SetEndpoint(server);

        var response = await ExecuteAsync("GetSkillProject", new JObject { ["projectSpaceGuid"] = ProjectGuid });

        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual(stable ? 3 : null, response.Result.Value<int?>("projectSpaceVersion"));
        Assert.AreEqual("$Project::InputDirectory/data", response.Result["inputFieldMetadata"]!["Path"]!.Value<string>("defaultValue"));
        var contentRequest = server.Requests.Single(request => request.Path.Contains("content.php"));
        StringAssert.StartsWith(contentRequest.Path, stable ? "/project_version_content.php?" : "/project_content.php?");
        if (stable)
            StringAssert.Contains(contentRequest.Path, "version=3");
    }

    [TestMethod]
    public async Task RemoteExecution_RequiresApprovalWithPinnedVersionAndDefaults()
    {
        using var server = CreateProjectServer(stable: true);
        SetEndpoint(server);
        ToolRequest? confirmation = null;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = request =>
            {
                confirmation = request;
                return false;
            }
        };

        var response = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = "ExecuteExternalProject",
            Arguments = new JObject { ["projectSpaceGuid"] = ProjectGuid, ["reason"] = "Inspect the reusable test function" }
        }, CancellationToken.None);

        Assert.IsNotNull(confirmation);
        Assert.AreEqual(3, confirmation.Arguments.Value<int>("projectSpaceVersion"));
        Assert.IsNotNull(confirmation.ConfirmationInputParameters);
        StringAssert.EndsWith(confirmation.ConfirmationInputParameters["Path"], "/data");
        Assert.IsTrue(File.Exists(confirmation.Arguments.Value<string>("confirmationProjectFile")));
        Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
        Assert.IsFalse(server.Requests.Any(request => request.Path.StartsWith("/project_content.php")));
    }

    [TestMethod]
    public async Task PrivateSearch_WithoutLoginExplainsRememberedCredentials()
    {
        using var server = new KnowledgeHttpServer(_ => new KnowledgeHttpResponse());
        SetEndpoint(server);

        var response = await ExecuteAsync("GetPSProjects", new JObject { ["visibility"] = "private" });

        Assert.IsFalse(response.Ok);
        StringAssert.Contains(response.Error, "Remember credentials");
        Assert.IsEmpty(server.Requests);
    }

    private static Task<ToolResponse> ExecuteAsync(string tool, JObject args) =>
        new DefaultToolApi().ExecuteAsync(new ToolRequest { ToolName = tool, Arguments = args }, CancellationToken.None);

    private static void SetEndpoint(KnowledgeHttpServer server) =>
        FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl).Value = server.BaseUrl;

    private static KnowledgeHttpServer CreateProjectServer(bool stable)
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            Name = "Path", UserField = true, UserFieldType = UserFieldTypes.Input,
            DefaultValue = "$Project::InputDirectory/data"
        };
        var project = new FlowBloxProject { ProjectName = "Reusable helper", UserFields = [field], FlowBlocks = [] };
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("project_file.json").Open()))
            writer.Write(JsonConvert.SerializeObject(project, JsonSettings.ProjectExport()));
        var content = JsonConvert.SerializeObject(new { Content = Convert.ToBase64String(bytes.ToArray()) });
        return new KnowledgeHttpServer(request => new KnowledgeHttpResponse
        {
            Body = request.Path.Contains("content.php")
                ? content
                : JsonConvert.SerializeObject(new
                {
                    Guid = ProjectGuid, Name = "Reusable helper", Description = "Reusable test function", Visibility = "Public",
                    Versions = stable ? new[] { new { VersionNumber = 1 }, new { VersionNumber = 3 }, new { VersionNumber = 2 } } : []
                })
        });
    }
}
