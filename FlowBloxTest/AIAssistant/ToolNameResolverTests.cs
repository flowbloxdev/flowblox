using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ToolNameResolverTests
{
    [TestMethod]
    [DataRow("GetSkillProject")]
    [DataRow("getskillproject")]
    [DataRow("FlowBloxAIToolApi-GetSkillProject")]
    [DataRow("flowblox-getskillproject")]
    [DataRow("mcp__FlowBlox__GetSkillProject")]
    [DataRow("ai__FlowBlox__getskillproject")]
    [DataRow("FlowBlox.Tools.GetSkillProject")]
    [DataRow("FlowBlox::GetSkillProject")]
    [DataRow("FlowBlox:GetSkillProject")]
    [DataRow("FlowBlox/GetSkillProject")]
    [DataRow("namespace_SkillProject")]
    public void Resolve_UsesOrderedNameStrategies(string requestedName)
    {
        var resolver = new ToolNameResolver(["GetSkillProject", "GetSkillProjects"]);

        Assert.AreEqual("GetSkillProject", resolver.Resolve(requestedName));
    }

    [TestMethod]
    [DataRow("create_issue", "create_issue")]
    [DataRow("mcp__jira__create_issue", "create_issue")]
    [DataRow("mssql.columnDiscovery", "mssql.columnDiscovery")]
    [DataRow("mcp__db__mssql.columnDiscovery", "mssql.columnDiscovery")]
    public void Resolve_PreservesSeparatorsInsideRegisteredNames(string requestedName, string expectedName)
    {
        var resolver = new ToolNameResolver(["create_issue", "issue", "mssql.columnDiscovery", "columnDiscovery"]);

        Assert.AreEqual(expectedName, resolver.Resolve(requestedName));
    }

    [TestMethod]
    public void Resolve_RejectsAmbiguousPartialNames()
    {
        var resolver = new ToolNameResolver(["GetSkillProject", "DeleteSkillProject"]);

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => resolver.Resolve("mcp__SkillProject"));

        StringAssert.Contains(error.Message, "Ambiguous tool");
        StringAssert.Contains(error.Message, "exact name");
        Assert.AreEqual("GetSkillProject", resolver.Resolve("GetSkillProject"));
        Assert.IsNull(resolver.Resolve("mcp__UnknownTool"));
        Assert.IsNull(resolver.Resolve("mcp__"));
    }

    [TestMethod]
    public void Resolve_PrefersExactNameOverCaseInsensitiveMatches()
    {
        var resolver = new ToolNameResolver(["Tool", "TOOL"]);

        Assert.AreEqual("Tool", resolver.Resolve("Tool"));
        Assert.ThrowsExactly<InvalidOperationException>(() => resolver.Resolve("tool"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Execute_ResolvesNamesBeforeApprovalIncludingBatchChildren(bool inBatch)
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
        var request = new ToolRequest
        {
            ToolName = "mcp__FlowBlox__executeinputfilecommand",
            Arguments = new JObject { ["key"] = "unused" }
        };
        if (inBatch)
        {
            request = new ToolRequest
            {
                ToolName = "flowblox-BatchExecuteToolRequests",
                Arguments = new JObject
                {
                    ["requests"] = new JArray(new JObject
                    {
                        ["toolName"] = request.ToolName,
                        ["arguments"] = request.Arguments
                    })
                }
            };
        }

        var response = await api.ExecuteAsync(request, CancellationToken.None);

        Assert.IsFalse(response.Ok);
        Assert.IsNotNull(confirmation);
        Assert.AreEqual("ExecuteInputFileCommand", confirmation.ToolName);
        Assert.AreEqual("unused", confirmation.Arguments.Value<string>("key"));
        var result = inBatch ? response.Result["batchResults"]![0]!["result"]! : response.Result;
        Assert.IsTrue(result.Value<bool>("cancelledByUser"));
    }

    [TestMethod]
    public async Task Execute_RejectsQualifiedNestedBatchAndUnknownNames()
    {
        var api = new DefaultToolApi();
        var response = await api.ExecuteAsync(new ToolRequest
        {
            ToolName = "BatchExecuteToolRequests",
            Arguments = new JObject
            {
                ["requests"] = new JArray(new JObject
                {
                    ["toolName"] = "mcp__FlowBlox__BatchExecuteToolRequests",
                    ["arguments"] = new JObject()
                })
            }
        }, CancellationToken.None);

        Assert.IsFalse(response.Ok);
        Assert.AreEqual("Nested batch not supported.", response.Result["batchResults"]![0]!.Value<string>("error"));

        var unknown = await api.ExecuteAsync(new ToolRequest { ToolName = "mcp__UnknownTool" }, CancellationToken.None);
        Assert.AreEqual("Tool 'mcp__UnknownTool' is not registered.", unknown.Error);
    }
}
