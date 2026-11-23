using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBloxTest.Services;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class AiAssistantSummaryCompactionTests
{
    [TestMethod]
    public async Task Generate_CompactsRestoredSummaryWithoutConsumingUnmergedMessages()
    {
        var executor = new RecordingAiExecutor();
        var service = CreateService(executor);
        service.RestoreSession(new AiAssistantHistoryDocument
        {
            ConversationSummary = new string('s', 101),
            SessionMessages = new List<AssistantSessionMessage>
            {
                new AssistantSingleMessage { MessageRole = "user", Message = "UNMERGED-HISTORY-MESSAGE" }
            }
        });

        await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None);

        var compaction = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantCompaction");
        StringAssert.Contains(compaction.Messages.Single().Content, "CURRENT-TASK");
        Assert.IsFalse(compaction.Messages.Single().Content.Contains("UNMERGED-HISTORY-MESSAGE", StringComparison.Ordinal));
        Assert.AreEqual("[COMPACTED]", service.CurrentConversationSummary);
        Assert.AreEqual("FlowBloxAIAssistantCompaction", executor.Requests[0].Source);
        Assert.AreEqual(0, executor.Requests.Count(request => request.Source == "FlowBloxAIAssistantSummary"));
        var history = new AiAssistantHistoryDocument();
        service.UpdateHistorySessionMetadata(history);
        Assert.AreEqual(0, history.SummarizedMessageCount);
        Assert.AreEqual(3, history.SessionMessages.Count);
        Assert.AreEqual("UNMERGED-HISTORY-MESSAGE", history.SessionMessages[0].CompleteMessage);
    }

    [TestMethod]
    [DataRow(99)]
    [DataRow(100)]
    public async Task Generate_DoesNotCompactSummaryAtOrBelowThreshold(int summaryLength)
    {
        var executor = new RecordingAiExecutor();
        var service = CreateService(executor);
        var summary = new string('s', summaryLength);
        service.RestoreSession(new AiAssistantHistoryDocument { ConversationSummary = summary });

        await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None);

        Assert.AreEqual(1, executor.Requests.Count);
        Assert.AreEqual(summary, service.CurrentConversationSummary);
    }

    [TestMethod]
    public async Task Generate_CompactsAfterMergingAndBeforeTheWorkingRequest()
    {
        var executor = new RecordingAiExecutor();
        var config = CreateConfiguration();
        config.MaxLatestMessages = 2;
        var service = CreateService(executor, config);
        service.RestoreSession(new AiAssistantHistoryDocument
        {
            SessionMessages = new List<AssistantSessionMessage>
            {
                new AssistantSingleMessage { MessageRole = "user", Message = new string('a', 150) },
                new AssistantSingleMessage { MessageRole = "assistant", Message = "OLDER-ANSWER" },
                new AssistantSingleMessage { MessageRole = "user", Message = "LATEST-USER-MESSAGE" },
                new AssistantSingleMessage { MessageRole = "assistant", Message = "LATEST-ANSWER" }
            }
        });

        await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None);

        Assert.AreEqual("FlowBloxAIAssistantSummary", executor.Requests[0].Source);
        Assert.AreEqual("FlowBloxAIAssistantCompaction", executor.Requests[1].Source);
        Assert.AreEqual(executor.ChatRequests.Single(), executor.Requests[2]);
        var compactedInput = executor.Requests[1].Messages.Single().Content;
        StringAssert.Contains(compactedInput, "OLDER-ANSWER");
        Assert.IsFalse(compactedInput.Contains("LATEST-USER-MESSAGE", StringComparison.Ordinal));
        var history = new AiAssistantHistoryDocument();
        service.UpdateHistorySessionMetadata(history);
        Assert.AreEqual(2, history.SummarizedMessageCount);
        Assert.AreEqual(6, history.SessionMessages.Count);
    }

    [TestMethod]
    public async Task Generate_RetriesUntilConfiguredTargetIsReachedEvenBelowThreshold()
    {
        var executor = new RecordingAiExecutor();
        var attempts = 0;
        executor.CompactionResponse = _ => new AiExecutorResult
        {
            Success = true,
            OutputText = new string('c', ++attempts == 1 ? 55 : 35)
        };
        var configuration = CreateConfiguration();
        configuration.SummaryCompactionTargetRatio = 0.4d;
        var service = CreateService(executor, configuration);
        service.RestoreSession(new AiAssistantHistoryDocument { ConversationSummary = new string('s', 101) });

        await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None);

        Assert.AreEqual(2, attempts);
        Assert.AreEqual(new string('c', 35), service.CurrentConversationSummary);
        var workingSummary = executor.ChatRequests.Single().Messages.Single(message => message.Role == "summary").Content;
        StringAssert.Contains(workingSummary, new string('c', 35));
    }

    [TestMethod]
    public async Task Generate_DeliversMessagesStagedDuringCompactionToTheNextWorkingRequest()
    {
        var runControl = new AssistantRunControl();
        var executor = new RecordingAiExecutor
        {
            CompactionResponse = _ =>
            {
                Assert.IsTrue(runControl.TryStageUserMessage("STAGED-DURING-COMPACTION"));
                return new AiExecutorResult { Success = true, OutputText = "COMPACTED" };
            }
        };
        var service = CreateService(executor);
        service.RestoreSession(new AiAssistantHistoryDocument { ConversationSummary = new string('s', 101) });

        await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None, runControl);

        Assert.AreEqual(1, executor.ChatRequests.Single().Messages.Count(message =>
            message.Role == "user" && message.Content == "STAGED-DURING-COMPACTION"));
        var compactionInput = executor.Requests[0].Messages.Single().Content;
        Assert.IsFalse(compactionInput.Contains("STAGED-DURING-COMPACTION", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Generate_BoundsRetriesAndPreservesSummaryWhenCompactionDoesNotShrinkIt()
    {
        var executor = new RecordingAiExecutor
        {
            CompactionResponse = _ => new AiExecutorResult { Success = true, OutputText = new string('c', 200) }
        };
        var service = CreateService(executor);
        var original = new string('s', 101);
        service.RestoreSession(new AiAssistantHistoryDocument { ConversationSummary = original });

        await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None);

        Assert.AreEqual(2, executor.Requests.Count(request => request.Source == "FlowBloxAIAssistantCompaction"));
        Assert.AreEqual(original, service.CurrentConversationSummary);
    }

    [TestMethod]
    [DataRow(false, "ignored")]
    [DataRow(true, " ")]
    public async Task Generate_PreservesSummaryWhenCompactionFailsOrReturnsEmptyOutput(bool success, string output)
    {
        var executor = new RecordingAiExecutor
        {
            CompactionResponse = _ => new AiExecutorResult { Success = success, OutputText = output }
        };
        var service = CreateService(executor);
        var original = new string('s', 101);
        service.RestoreSession(new AiAssistantHistoryDocument { ConversationSummary = original });

        var result = await service.ExecutePromptAsync("CURRENT-TASK", CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(original, service.CurrentConversationSummary);
        Assert.AreEqual(1, executor.Requests.Count(request => request.Source == "FlowBloxAIAssistantCompaction"));
    }

    private static AssistantConfiguration CreateConfiguration()
    {
        return new AssistantConfiguration
        {
            MaxSummaryTokens = 100,
            SummaryCompactionTargetRatio = 0.6d,
            ApproximateCharactersPerToken = 1,
            MaxContextTokens = 100000,
            MaxLatestMessages = 16,
            MinLatestMessages = 2,
            EnableAutomaticAdjustment = false
        };
    }

    private static AiAssistantService CreateService(RecordingAiExecutor executor, AssistantConfiguration? config = null)
    {
        var configuration = config ?? CreateConfiguration();
        return new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration);
    }
}
