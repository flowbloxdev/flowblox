using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
public class ToolArgumentValidationTests
{
    [TestMethod]
    [DataRow("GetLastDebugArtefact")]
    [DataRow("SearchFlowBlock")]
    [DataRow("RunProjectDebugTest")]
    [DataRow("BatchExecuteToolRequests")]
    public async Task Execute_RejectsExtraArgumentWrapperBeforeHandlerExecution(string toolName)
    {
        var response = await new DefaultToolApi().ExecuteAsync(
            new ToolRequest
            {
                ToolName = toolName,
                Arguments = new JObject
                {
                    ["arguments"] = new JObject
                    {
                        ["fieldChangeId"] = 2
                    }
                }
            },
            CancellationToken.None);

        Assert.IsFalse(response.Ok);
        StringAssert.Contains(response.Error, $"Invalid argument structure for tool '{toolName}'");
        StringAssert.Contains(response.Error, "directly in toolCalls[i].arguments");
        StringAssert.Contains(response.Error, "The tool was not executed.");
    }

    [TestMethod]
    public async Task Execute_RejectsWrappedBatchChildBeforeRequestingApproval()
    {
        var confirmationRequested = false;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = _ =>
            {
                confirmationRequested = true;
                return false;
            }
        };

        var response = await api.ExecuteAsync(
            new ToolRequest
            {
                ToolName = "BatchExecuteToolRequests",
                Arguments = new JObject
                {
                    ["requests"] = new JArray(new JObject
                    {
                        ["toolName"] = "ExecuteInputFileCommand",
                        ["arguments"] = new JObject
                        {
                            ["arguments"] = new JObject
                            {
                                ["key"] = "unused"
                            }
                        }
                    })
                }
            },
            CancellationToken.None);

        Assert.IsFalse(response.Ok);
        Assert.IsFalse(confirmationRequested);
        var childError = response.Result["batchResults"]![0]!.Value<string>("error");
        StringAssert.Contains(childError, "Invalid argument structure");
    }
}
