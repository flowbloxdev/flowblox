using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools.Handler;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class BatchExecuteToolRequestsHandlerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Batch_ForwardsAllParameters_WithOrWithoutArgumentsWrapper(bool wrapped)
    {
        var received = new List<ToolRequest>();
        var handler = new BatchExecuteToolRequestsHandler((request, _) =>
        {
            received.Add(request);
            return Task.FromResult(new ToolResponse { Ok = true });
        });
        var requests = new JArray();
        for (var index = 0; index < 5; index++)
        {
            var parameters = new JObject
            {
                ["typeFullName"] = $"Synthetic.Type{index}",
                ["settings"] = new JObject { ["enabled"] = false },
                ["paths"] = new JArray("first", "second")
            };
            var request = wrapped ? new JObject { ["arguments"] = parameters } : parameters;
            request["toolName"] = "GetTypeKindsInfo";
            requests.Add(request);
        }

        var original = requests.DeepClone();
        var response = await handler.HandleAsync(new JObject { ["requests"] = requests }, CancellationToken.None);

        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual(5, received.Count);
        for (var index = 0; index < received.Count; index++)
        {
            Assert.AreEqual($"Synthetic.Type{index}", received[index].Arguments.Value<string>("typeFullName"));
            Assert.IsNull(received[index].Arguments["toolName"]);
            Assert.IsFalse(received[index].Arguments["settings"]!.Value<bool>("enabled"));
            Assert.AreEqual(2, received[index].Arguments["paths"]!.Count());
        }

        Assert.IsTrue(JToken.DeepEquals(original, requests));
    }

    [TestMethod]
    public async Task ExplicitArguments_TakePrecedenceOverRootParameters()
    {
        JObject? received = null;
        var handler = new BatchExecuteToolRequestsHandler((request, _) =>
        {
            received = request.Arguments;
            return Task.FromResult(new ToolResponse { Ok = true });
        });
        var response = await handler.HandleAsync(new JObject
        {
            ["requests"] = new JArray(new JObject
            {
                ["toolName"] = "GetTypeKindsInfo",
                ["typeFullName"] = "Ignored.RootType",
                ["arguments"] = new JObject { ["typeFullName"] = "Explicit.Type" }
            })
        }, CancellationToken.None);

        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual("Explicit.Type", received!.Value<string>("typeFullName"));
        Assert.AreEqual(1, received.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidArguments_FailWithoutExecution_AndRespectContinueOnError(bool continueOnError)
    {
        var executions = 0;
        var handler = new BatchExecuteToolRequestsHandler((_, _) =>
        {
            executions++;
            return Task.FromResult(new ToolResponse { Ok = true });
        });
        var response = await handler.HandleAsync(new JObject
        {
            ["continueOnError"] = continueOnError,
            ["requests"] = new JArray
            {
                new JObject { ["toolName"] = "GetTypeKindsInfo", ["arguments"] = "invalid" },
                new JObject { ["toolName"] = "GetTypeKindsInfo", ["typeFullName"] = "Synthetic.Type" }
            }
        }, CancellationToken.None);

        Assert.IsFalse(response.Ok);
        Assert.AreEqual(continueOnError ? 1 : 0, executions);
        StringAssert.Contains(response.Result["batchResults"]![0]!.Value<string>("error"), "arguments must be a JSON object");
    }
}
