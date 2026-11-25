using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantContextBudgetTests
{
    [TestMethod]
    [DataRow(48000)]
    [DataRow(0)]
    public void ContextBudget_RespectsSmallerModelAndLargerConfiguredOutputReserve(int contextLimit)
    {
        var budget = AssistantTokenBudget.Create(
            new AssistantConfiguration { MaxContextTokens = contextLimit, ReservedResponseTokens = 4096, MaxTokens = 8000 },
            new ModelPricing { MaxInputTokens = 32000 });
        Assert.AreEqual(32000, budget.MaxContextTokens);
        Assert.AreEqual(8000, budget.ReservedResponseTokens);
    }

    [TestMethod]
    public void ContextSelection_CountsCachedSystemContentAtFullSize()
    {
        var request = AssistantChatRequestBuilder.Build(
            new string('s', 80),
            "bootstrap",
            "",
            [
                new AssistantSingleMessage { MessageRole = "user", Message = new string('a', 20) },
                new AssistantSingleMessage { MessageRole = "assistant", Message = new string('b', 20) }
            ],
            "current",
            20,
            0,
            null,
            new AssistantTokenBudget { MaxContextTokens = 100, ReservedResponseTokens = 10, ApproximateCharactersPerToken = 1 });
        Assert.AreEqual(1, request.IncludedHistoryMessageCount);
        Assert.AreEqual(1, request.FirstIncludedHistoryMessageIndex);
    }

    [TestMethod]
    public void ContextEstimation_CalibrationRaisesTechnicalCountWithoutChangingWindowSize()
    {
        var configuration = new AssistantConfiguration();
        var original = AssistantTokenBudget.Create(configuration, null);
        var calibrated = AssistantTokenBudget.Create(configuration, null, 1.5d);
        Assert.AreEqual(original.MaxContextTokens, calibrated.MaxContextTokens);
        Assert.AreEqual(30, calibrated.EstimateTokens(new string('x', 100)));
    }
}
