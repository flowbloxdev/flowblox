using System.Globalization;
using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBloxTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantActivityTests
{
    [TestMethod]
    [DataRow("de-DE", false)]
    [DataRow("en-US", false)]
    [DataRow("de-DE", true)]
    public async Task EmptyActivity_IsRepairedInTranscriptAndHistoryWithoutExtraRequests(string culture, bool batch)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var arguments = batch
            ? new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject { ["toolName"] = "SearchFlowBlock", ["arguments"] = new JObject { ["searchForNames"] = "Fixture" } },
                    new JObject { ["toolName"] = "GetTypeKindsInfo", ["arguments"] = new JObject { ["typeFullName"] = "Fixture.Type" } },
                    new JObject { ["toolName"] = "GetComponentSnapshot", ["name"] = "Fixture" }
                }
            }
            : new JObject { ["typeFullName"] = "Fixture.Type" };
        var name = batch ? "BatchExecuteToolRequests" : "GetTypeKindsInfo";
        var executor = new QueuedAiExecutor(BuildInstruction(" \n ", name, arguments), FinalResponse);
        var tools = CreateTools();
        var service = CreateService(executor, tools);
        try
        {
            var result = await service.ExecutePromptAsync("Inspect the fixture", CancellationToken.None);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, executor.Requests.Count);
            var text = result.TranscriptLines.First(line => line.Kind == AssistantTranscriptKind.Assistant).Text;
            var inspection = culture == "de-DE"
                ? "Ich lese die benötigten Informationen und Konfigurationen für die nächsten Schritte."
                : "I am reading the information and configuration needed for the next steps.";
            var expected = batch ? "Ich suche nach passenden Komponenten, Skills und Beispielen. " + inspection : inspection;
            Assert.AreEqual(expected, text);
            var history = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(history);
            var pair = history.SessionMessages.OfType<AssistantMessagePair>().Single();
            var normalized = JObject.Parse(pair.AssistantRequest);
            Assert.AreEqual(text, normalized.Value<string>("assistantMessage"));
            Assert.IsTrue(JToken.DeepEquals(arguments, normalized["toolCalls"]![0]!["arguments"]));
            Assert.IsTrue(executor.ChatRequests[1].Messages.Any(message => message.Content.Contains(text, StringComparison.Ordinal)));
            await tools.Received(1).ExecuteAsync(
                Arg.Is<ToolRequest>(request => request.ToolName == name && JToken.DeepEquals(request.Arguments, arguments)),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            service.ResetSession();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [TestMethod]
    public async Task ProvidedActivity_IsPreservedInTranscriptAndPersistedInstruction()
    {
        const string activity = "I am inspecting the fixture to identify the required settings.";
        var executor = new QueuedAiExecutor(BuildInstruction(activity, "GetComponentSnapshot", new JObject()), FinalResponse);
        var service = CreateService(executor, CreateTools());
        try
        {
            var result = await service.ExecutePromptAsync("Inspect the fixture", CancellationToken.None);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(activity, result.TranscriptLines.First(line => line.Kind == AssistantTranscriptKind.Assistant).Text);
            var history = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(history);
            var instruction = JObject.Parse(history.SessionMessages.OfType<AssistantMessagePair>().Single().AssistantRequest);
            Assert.AreEqual(activity, instruction.Value<string>("assistantMessage"));
            Assert.AreEqual(2, executor.Requests.Count);
        }
        finally
        {
            service.ResetSession();
        }
    }

    private const string FinalResponse = """{"assistantMessage":"Done","final":true,"toolCalls":[]}""";

    private static IFlowBloxAIToolApi CreateTools()
    {
        var tools = Substitute.For<IFlowBloxAIToolApi>();
        tools.GetToolDefinitions().Returns(Array.Empty<ToolDefinition>());
        tools.ExecuteAsync(Arg.Any<ToolRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ToolResponse { Ok = true }));
        return tools;
    }

    private static AiAssistantService CreateService(QueuedAiExecutor executor, IFlowBloxAIToolApi tools) => new(
        executor,
        tools,
        configurationProvider: () => new AssistantConfiguration
        {
            MaxContextTokens = 1000000,
            MaxLatestMessages = 50,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = false
        });

    private static string BuildInstruction(string message, string name, JObject arguments) => new JObject
    {
        ["assistantMessage"] = message,
        ["final"] = false,
        ["toolCalls"] = new JArray { new JObject { ["toolName"] = name, ["arguments"] = arguments } }
    }.ToString(Formatting.None);
}
