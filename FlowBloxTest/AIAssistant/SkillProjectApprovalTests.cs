using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Core.Util;
using FlowBlox.Core.Constants;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class SkillProjectApprovalTests
{
    private FlowBloxProject? _previousProject;
    private string _directory = null!;
    private string _previousApprovals = null!;
    private string? _previousLocalAppData;

    [TestInitialize]
    public void Initialize()
    {
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject { ProjectName = "Main" };
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxApprovalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var option = FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName);
        _previousApprovals = option.PersistentValue;
        option.PersistentValue = string.Empty;
        _previousLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
        FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName).PersistentValue = _previousApprovals;
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _previousLocalAppData);
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    [DataRow("ExecuteExternalProject")]
    [DataRow("RunProjectDebugTest")]
    public async Task SqlTemplate_RequiresApprovalAndShowsActualSql(string toolName)
    {
        ToolRequest? confirmation = null;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = request =>
            {
                confirmation = request;
                return false;
            }
        };
        var path = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "ExecuteSQLQuery.fbprj");
        var response = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = toolName,
            Arguments = new JObject
            {
                ["projectFile"] = path,
                ["reason"] = "Review the database operation",
                ["inputParameters"] = new JObject
                {
                    ["Input_DbType"] = "SQLite",
                    ["Input_SQLConnectionString"] = "Data Source=review-only.db",
                    ["Input_SQLQuery"] = "DELETE FROM Customers;"
                }
            }
        }, CancellationToken.None);

        Assert.IsNotNull(confirmation);
        Assert.AreEqual("DELETE FROM Customers;", confirmation.ConfirmationInputParameters!["Input_SQLQuery"]);
        Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
        Assert.IsFalse(File.Exists(Path.Combine(_directory, "review-only.db")));
    }

    [TestMethod]
    [DataRow("ExecuteExternalProject")]
    [DataRow("RunProjectDebugTest")]
    public async Task ApprovedDistributedTemplate_DoesNotRequestApproval(string toolName)
    {
        FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName).PersistentValue = "Other.fbprj\r\n  listfiles.fbprj \r\n";
        var asked = false;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = _ =>
            {
                asked = true;
                return false;
            }
        };
        var path = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "ListFiles.fbprj");
        var response = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = toolName,
            Arguments = new JObject { ["projectFile"] = path }
        }, CancellationToken.None);

        Assert.IsFalse(asked);
        Assert.IsFalse(response.Ok);
        Assert.IsFalse(response.Result.Value<bool?>("cancelledByUser") == true);
        StringAssert.Contains(response.Error, "reason is required");
    }

    [TestMethod]
    public async Task UnapprovedCustomProject_RequiresApprovalAndFreezesResolvedValuesAndDefaults()
    {
        var main = FlowBloxProjectManager.Instance.ActiveProject;
        var source = main.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, fieldName: "Sql");
        source.StringValue = "DELETE FROM Customers;";

        var skill = new FlowBloxProject { ProjectName = "Custom" };
        FlowBloxProjectManager.Instance.ActiveProject = skill;
        var sql = skill.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, fieldName: "Sql");
        sql.StoreValueLocally = false;
        var encoding = skill.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, fieldName: "Encoding");
        encoding.StoreValueLocally = false;
        encoding.StringValue = "UTF8";
        var path = Path.Combine(_directory, "custom.fbprj");
        skill.Save(path);
        FlowBloxProjectManager.Instance.ActiveProject = main;

        ToolRequest? confirmation = null;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = request =>
            {
                confirmation = request;
                source.StringValue = "CHANGED AFTER CONFIRMATION";
                return false;
            }
        };
        var original = new ToolRequest
        {
            ToolName = "ExecuteExternalProject",
            Arguments = new JObject
            {
                ["projectFile"] = path,
                ["reason"] = "Review custom execution",
                ["inputParameters"] = new JObject { ["Sql"] = source.FullyQualifiedName }
            }
        };
        var response = await api.ExecuteAsync(original, CancellationToken.None);

        Assert.IsNotNull(confirmation);
        Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
        Assert.AreEqual("DELETE FROM Customers;", confirmation.ConfirmationInputParameters!["Sql"]);
        Assert.AreEqual("UTF8", confirmation.ConfirmationInputParameters["Encoding"]);
        var runnerInputs = ProjectExecutionToolUtilities.ResolveInputParameters(confirmation.Arguments["inputParameters"] as JObject, skill);
        Assert.AreEqual(confirmation.ConfirmationInputParameters["Sql"], runnerInputs["Sql"]);
        Assert.AreEqual(confirmation.ConfirmationInputParameters["Encoding"], runnerInputs["Encoding"]);
        Assert.AreEqual(source.FullyQualifiedName, original.Arguments["inputParameters"]!.Value<string>("Sql"));
    }

    [TestMethod]
    public async Task UnapprovedDistributedTemplate_FailsClosedWithoutCallback()
    {
        var response = await new DefaultToolApi().ExecuteAsync(new ToolRequest
        {
            ToolName = "ExecuteExternalProject",
            Arguments = new JObject
            {
                ["projectFile"] = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "ExecuteSQLQuery.fbprj"),
                ["reason"] = "Database query"
            }
        }, CancellationToken.None);
        Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
    }

    [TestMethod]
    public async Task Approval_IsPersistedAndSkipsFurtherPromptsUntilUserRevokesIt()
    {
        var path = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "ListFiles.fbprj")!;
        var count = 0;
        var approve = true;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = request =>
            {
                count++;
                request.RememberProjectApproval = approve;
                return approve;
            }
        };
        var request = new ToolRequest
        {
            ToolName = "ExecuteExternalProject",
            Arguments = new JObject { ["projectFile"] = path }
        };
        // Missing reason stops at handler validation; no project is actually run.
        await api.ExecuteAsync(request, CancellationToken.None);
        Assert.AreEqual(1, count);
        Assert.IsTrue(SkillProjectApprovalStore.IsApproved(path));
        await api.ExecuteAsync(request, CancellationToken.None);
        Assert.AreEqual(1, count);

        SkillProjectApprovalStore.Approve(path);
        SkillProjectApprovalStore.Approve(DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "GetFileContent.fbprj")!);
        var xml = new XmlDocument();
        xml.Load(GlobalPaths.GlobalConfigurationXml);
        var stored = xml.SelectSingleNode($"//option_element[@name='{SkillProjectApprovalStore.OptionName}']/@value")!.Value!;
        CollectionAssert.AreEqual(new[] { "ListFiles.fbprj", "GetFileContent.fbprj" }, stored.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName).PersistentValue = string.Empty;
        approve = false;
        var response = await api.ExecuteAsync(request, CancellationToken.None);
        Assert.AreEqual(2, count);
        Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
    }

    [TestMethod]
    public async Task CustomProjectWithTemplateFileName_DoesNotInheritItsApproval()
    {
        FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName).PersistentValue = "ListFiles.fbprj";
        var path = Path.Combine(_directory, "ListFiles.fbprj");
        new FlowBloxProject { ProjectName = "Custom" }.Save(path);
        var asked = false;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = _ =>
            {
                asked = true;
                return false;
            }
        };
        var response = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = "ExecuteExternalProject",
            Arguments = new JObject { ["projectFile"] = path, ["reason"] = "Review this custom project" }
        }, CancellationToken.None);
        Assert.IsTrue(asked);
        Assert.IsTrue(response.Result.Value<bool>("cancelledByUser"));
    }

    [TestMethod]
    public async Task ActiveSkillProject_RemembersApprovalWhenProjectFileArgumentIsEmpty()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var localDataOption = FlowBloxOptions.GetOptionInstance().GetOption("Paths.LocalAppDataDir");
        var previousLocalData = localDataOption.PersistentValue;
        localDataOption.PersistentValue = _directory;
        ToolHandlerUtilities.SetCurrentSessionGuid(sessionId);
        try
        {
            SkillProjectSessionStore.CreateOrUpdateFile(sessionId, "Active", new FlowBloxProject
            {
                ProjectName = "Active",
                ProjectDescription = "Approval regression test"
            });
            SkillProjectSessionStore.Edit(sessionId, "Active");
            var count = 0;
            var api = new DefaultToolApi
            {
                ToolExecutionConfirmationCallback = request =>
                {
                    count++;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(request.Arguments.Value<string>("confirmationProjectFile")));
                    request.RememberProjectApproval = true;
                    return true;
                }
            };
            var request = new ToolRequest
            {
                ToolName = "RunProjectDebugTest",
                Arguments = new JObject { ["projectFile"] = string.Empty }
            };
            // Validation rejects the missing reason before RunnerHost is started.
            await api.ExecuteAsync(request, CancellationToken.None);
            await api.ExecuteAsync(request, CancellationToken.None);
            Assert.AreEqual(1, count);
        }
        finally
        {
            SkillProjectSessionStore.Clear(sessionId);
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
            localDataOption.PersistentValue = previousLocalData;
        }
    }

    [TestMethod]
    public async Task AllowOnce_DoesNotPersistApprovalAndPromptsAgain()
    {
        var count = 0;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = _ =>
            {
                count++;
                return true;
            }
        };
        var path = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, "ListFiles.fbprj")!;
        var request = new ToolRequest
        {
            ToolName = "ExecuteExternalProject",
            Arguments = new JObject { ["projectFile"] = path }
        };
        // Missing reason stops at validation without launching RunnerHost.
        await api.ExecuteAsync(request, CancellationToken.None);
        await api.ExecuteAsync(request, CancellationToken.None);
        Assert.AreEqual(2, count);
        Assert.IsFalse(SkillProjectApprovalStore.IsApproved(path));
        Assert.AreEqual(string.Empty, FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName).PersistentValue);
        Assert.IsFalse(File.Exists(GlobalPaths.GlobalConfigurationXml));
    }

    [TestMethod]
    public void AssistantCannotGrantItsOwnApprovalThroughOptionsTool()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => ToolHandlerUtilities.SetOptionValue(new JObject
        {
            ["key"] = SkillProjectApprovalStore.OptionName,
            ["stringValue"] = "ExecuteSQLQuery.fbprj"
        }));
        Assert.AreEqual(string.Empty, FlowBloxOptions.GetOptionInstance().GetOption(SkillProjectApprovalStore.OptionName).PersistentValue);
    }
}
