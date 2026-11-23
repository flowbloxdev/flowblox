using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantPauseTests
{
    private const string ToolRound = """
        {"assistantMessage":"Working","final":false,"toolCalls":[
          {"toolName":"Record","arguments":{"id":1}},
          {"toolName":"Record","arguments":{"id":2}}
        ]}
        """;
    private const string FinalRound = """
        {"assistantMessage":"Updated using your new message.","final":true,"toolCalls":[]}
        """;

    [TestMethod]
    public async Task PauseDuringAiRequest_FinishesResponseAndAllToolsWithoutCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new AssistantRunControl();
        var requestNumber = 0;
        var executor = new DelegateExecutor(async (
            _,
            ct) =>
        {
            if (Interlocked.Increment(ref requestNumber) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(ct);
                return new()
                {
                    Success = true,
                    OutputText = ToolRound
                };
            }

            return new()
            {
                Success = true,
                OutputText = FinalRound
            };
        });
        var tools = new RecordingToolApi();
        var service = CreateService(executor, tools);
        using var cancellation = new CancellationTokenSource();
        var run = service.ExecutePromptAsync(
            "Build something",
            cancellation.Token,
            control);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        control.RequestPause();
        release.SetResult();
        await WaitForPausedAsync(control);

        Assert.IsFalse(run.IsCompleted, "A paused run must keep its original task alive.");
        Assert.IsFalse(cancellation.IsCancellationRequested);
        CollectionAssert.AreEqual(new[] { 1, 2 }, tools.ExecutedIds);
        Assert.AreEqual(1, executor.Requests.Count);
        var pair = (AssistantMessagePair)service.GetDebugSnapshot().History.Last().Message;
        StringAssert.Contains(pair.ToolApiResponse, "TOOL-2");
        Assert.IsFalse(JObject.Parse(pair.AssistantRequest).Value<bool>("final"));

        control.Continue();
        var result = await run;

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.IsPaused);
        Assert.AreEqual(2, executor.Requests.Count);
        service.ResetSession();
    }

    [TestMethod]
    public async Task PauseDuringToolBatch_StagesMultipleUserMessagesBeforeContinuationRequest()
    {
        const string originalMessage = "Erstelle eine rote Tabelle.";
        const string insertedMessage = "Bitte blau statt rot.";
        const string secondInsertedMessage = "Und verwende eine kompakte Darstellung.";

        var control = new AssistantRunControl();
        var executor = new DelegateExecutor((
            _,
            _) => Task.FromResult(new AiExecutorResult
        {
            Success = true,
            OutputText = ToolRound
        }));
        var tools = new RecordingToolApi
        {
            OnExecute = _ => control.RequestPause()
        };
        var service = CreateService(executor, tools);
        var run = service.ExecutePromptAsync(
            originalMessage,
            CancellationToken.None,
            control);
        await WaitForPausedAsync(control);

        Assert.IsFalse(run.IsCompleted);
        Assert.AreEqual(2, tools.ExecutedIds.Count);
        Assert.IsTrue(control.TryStageUserMessage(insertedMessage));
        Assert.IsTrue(control.TryStageUserMessage(secondInsertedMessage));
        executor.Handle = (_, _) => Task.FromResult(new AiExecutorResult
        {
            Success = true,
            OutputText = FinalRound
        });
        control.Continue();
        var result = await run;

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, executor.Requests.Count);
        var resumedMessages = executor.Requests.Last().Messages.ToList();
        var originalMessageIndex = resumedMessages.FindIndex(message =>
            message.Role == "user" && message.Content.Contains(originalMessage, StringComparison.Ordinal));
        var toolResultIndex = resumedMessages.FindLastIndex(message => message.Role == "tool");
        var insertedMessages = resumedMessages
            .Where(message => message.Role == "user" &&
                (message.Content.Contains(insertedMessage, StringComparison.Ordinal) ||
                 message.Content.Contains(secondInsertedMessage, StringComparison.Ordinal)))
            .ToList();

        Assert.IsTrue(originalMessageIndex >= 0, "The original task must remain in the resumed request.");
        Assert.IsTrue(toolResultIndex > originalMessageIndex);
        var toolContext = string.Join("\n", resumedMessages.Where(message => message.Role == "tool")
            .Select(message => message.Content));
        StringAssert.Contains(toolContext, "TOOL-1");
        StringAssert.Contains(toolContext, "TOOL-2");
        Assert.AreEqual(2, insertedMessages.Count, "Every staged message must be sent exactly once.");
        Assert.IsTrue(resumedMessages.IndexOf(insertedMessages[0]) > toolResultIndex);
        Assert.IsTrue(resumedMessages.IndexOf(insertedMessages[1]) > resumedMessages.IndexOf(insertedMessages[0]));

        var persistedInsertedMessages = service.GetDebugSnapshot().History
            .Where(entry => entry.Message.Role == "user" &&
                entry.Message.CompleteMessage.Contains(insertedMessage, StringComparison.Ordinal))
            .ToList();
        Assert.AreEqual(1, persistedInsertedMessages.Count,
            "The inserted message must remain in session history after resuming.");
        Assert.AreEqual(1, service.GetDebugSnapshot().History.Count(entry =>
            entry.Message.Role == "user" &&
            entry.Message.CompleteMessage.Contains(secondInsertedMessage, StringComparison.Ordinal)));
        CollectionAssert.AreEqual(new[] { 1, 2 }, tools.ExecutedIds,
            "Resuming must not execute the completed tool calls again.");
        service.ResetSession();
    }

    [TestMethod]
    public async Task StopWhilePaused_CancelsWaitAndPreservesCompletedResponse()
    {
        var control = new AssistantRunControl();
        var executor = new DelegateExecutor((
            _,
            _) =>
        {
            control.RequestPause();
            return Task.FromResult(new AiExecutorResult
            {
                Success = true,
                OutputText = "MALFORMED RESPONSE"
            });
        });
        var service = CreateService(executor, new RecordingToolApi());
        using var cancellation = new CancellationTokenSource();
        var run = service.ExecutePromptAsync(
            "Request",
            cancellation.Token,
            control);
        await WaitForPausedAsync(control);
        Assert.AreEqual(1, executor.Requests.Count);
        Assert.IsFalse(run.IsCompleted);
        StringAssert.Contains(service.GetDebugSnapshot().History.Last().Message.CompleteMessage, "MALFORMED RESPONSE");

        cancellation.Cancel();
        try
        {
            await run;
            Assert.Fail("Stopping a paused run must cancel its wait.");
        }
        catch (OperationCanceledException) { }
        service.ResetSession();
    }

    [TestMethod]
    public async Task StopAfterPauseRequest_StillCancelsToolExecution()
    {
        var control = new AssistantRunControl();
        using var cancellation = new CancellationTokenSource();
        var executor = new DelegateExecutor((_, _) => Task.FromResult(new AiExecutorResult
        {
            Success = true,
            OutputText = ToolRound
        }));
        var tools = new RecordingToolApi
        {
            OnExecute = _ => { control.RequestPause(); cancellation.Cancel(); }
        };
        var service = CreateService(executor, tools);
        try
        {
            await service.ExecutePromptAsync(
                "Request",
                cancellation.Token,
                control);
            Assert.Fail("Stop must still cancel a pause-requested run.");
        }
        catch (OperationCanceledException) { }

        Assert.AreEqual(1, tools.ExecutedIds.Count);
        service.ResetSession();
    }

    private static AiAssistantService CreateService(
        IAiExecutor executor,
        IFlowBloxAIToolApi tools)
    {
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        return new AiAssistantService(
            executor,
            tools,
            configurationProvider: () => new AssistantConfiguration
            {
                MaxToolRounds = 4,
                MaxLatestMessages = 10,
                MinLatestMessages = 1,
                MaxContextTokens = 100000,
                EnableAutomaticAdjustment = false
            });
    }

    private static async Task WaitForPausedAsync(AssistantRunControl control)
    {
        if (control.IsPaused)
            return;

        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void StateChanged(object? sender, EventArgs args)
        {
            if (control.IsPaused)
                paused.TrySetResult();
        }

        control.StateChanged += StateChanged;
        try
        {
            if (control.IsPaused)
                return;
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            control.StateChanged -= StateChanged;
        }
    }

    private sealed class DelegateExecutor(Func<AIChatRequest, CancellationToken, Task<AiExecutorResult>> handle) : IAiExecutor
    {
        public Func<AIChatRequest, CancellationToken, Task<AiExecutorResult>> Handle { get; set; } = handle;
        public List<AIChatRequest> Requests { get; } = new();

        public Task<AiExecutorResult> ExecuteChatAsync(
            AIChatRequest request,
            AssistantConfiguration configuration,
            CancellationToken ct)
        {
            Requests.Add(request);
            return Handle(request, ct);
        }
    }

    private sealed class RecordingToolApi : IFlowBloxAIToolApi
    {
        public List<int> ExecutedIds { get; } = new();
        public Action<int>? OnExecute
        {
        get; init;
        }

        public IReadOnlyList<ToolDefinition> GetToolDefinitions() => [];

        public Task<ToolResponse> ExecuteAsync(
            ToolRequest request,
            CancellationToken ct)
        {
            var id = request.Arguments.Value<int>("id");
            ExecutedIds.Add(id);
            OnExecute?.Invoke(id);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ToolResponse
            {
                Ok = true,
                Result = new JObject
                {
                    ["marker"] = "TOOL-" + id
                }
            });
        }
    }
}
