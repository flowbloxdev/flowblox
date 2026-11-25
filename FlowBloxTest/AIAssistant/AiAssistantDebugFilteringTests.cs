using System.Windows.Threading;
using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.UICore.ViewModels;
using FlowBloxTest.Services;

namespace FlowBloxTest.AIAssistant;

[STATestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AiAssistantDebugFilteringTests
{
    [TestMethod]
    public void Filters_CombineKindsWithOrAndOriginWithAnd()
    {
        var filters = new AiAssistantSystemMessageFilters();
        Select(filters.Kinds, "Explanation", "ToolDefinition");
        Select(filters.Origins, "Dynamic");
        Assert.IsTrue(filters.Includes(Message(AssistantSystemMessageKind.Explanation, true)));
        Assert.IsTrue(filters.Includes(Message(AssistantSystemMessageKind.ToolDefinition, true)));
        Assert.IsFalse(filters.Includes(Message(AssistantSystemMessageKind.TypeInfo, true)));
        Assert.IsFalse(filters.Includes(Message(AssistantSystemMessageKind.Explanation, false)));
        filters.Origins.Single(option => option.Key == "Static").IsSelected = true;
        Assert.IsTrue(filters.Includes(Message(AssistantSystemMessageKind.Explanation, false)));
    }

    [TestMethod]
    public void BulkFilterCommands_NotifyOnceAndResetRestoresUnrestrictedView()
    {
        var filters = new AiAssistantSystemMessageFilters();
        Select(filters.Kinds, "ToolDefinition");
        Select(filters.Origins, "Dynamic");
        var updates = 0;
        filters.FiltersChanged += (_, _) => updates++;
        filters.SelectAllCommand.Execute(null!);
        Assert.AreEqual(1, updates);
        Assert.IsTrue(filters.Kinds.Concat(filters.Origins).All(option => option.IsSelected));
        Assert.IsTrue(filters.Includes(Message(AssistantSystemMessageKind.SessionBootstrap, false)));
        filters.ClearCommand.Execute(null!);
        Assert.AreEqual(2, updates);
        Assert.IsFalse(filters.Kinds.Concat(filters.Origins).Any(option => option.IsSelected));
        Assert.IsTrue(filters.Includes(Message(AssistantSystemMessageKind.TypeInfo, true)));
        Assert.IsTrue(filters.Includes(Message(AssistantSystemMessageKind.SystemPrompt, false)));
    }

    [TestMethod]
    public void SystemFilters_RecalculateVisibleTokensAndKeepContextTotalIndependent()
    {
        var service = CreateService();
        using var viewModel = new AiAssistantDebugViewModel(service, null!);
        ShowTopic(viewModel, AiAssistantDebugTopic.SystemMessages);
        var allTokens = viewModel.VisibleSystemMessageTokens;
        var contextTotal = viewModel.ContextStatistics;
        Select(viewModel.SystemMessageFilters.Kinds, "TypeInfo", "ToolDefinition");
        Select(viewModel.SystemMessageFilters.Origins, "Dynamic");
        CollectionAssert.AreEqual(new[] { "fixture:type", "fixture:tool" }, viewModel.Cards.Select(card => card.Key).ToArray());
        Assert.AreEqual(30L, viewModel.VisibleSystemMessageTokens);
        Assert.IsTrue(allTokens > viewModel.VisibleSystemMessageTokens);
        Assert.AreEqual(contextTotal, viewModel.ContextStatistics);
        viewModel.SystemMessageFilters.ClearCommand.Execute(null!);
        Assert.AreEqual(allTokens, viewModel.VisibleSystemMessageTokens);
    }

    [TestMethod]
    public void MultiSelection_UpdatesEditorAndSurvivesFilterRefreshByKey()
    {
        var service = CreateService();
        using var viewModel = new AiAssistantDebugViewModel(service, null!);
        ShowTopic(viewModel, AiAssistantDebugTopic.SystemMessages);
        var selection = viewModel.Cards.Where(card => card.IsPinned).Take(2).ToArray();
        viewModel.SetSelectedCards(selection);
        var selectedContent = viewModel.Editor.ContentText;
        Assert.AreEqual(2, viewModel.SelectedCards.Count);
        foreach (var card in selection)
            StringAssert.Contains(selectedContent, card.Content);
        var tokens = viewModel.SelectionTokens;
        Select(viewModel.SystemMessageFilters.Origins, "Dynamic");
        CollectionAssert.AreEqual(selection.Select(card => card.Key).ToArray(), viewModel.SelectedCards.Select(card => card.Key).ToArray());
        Assert.AreEqual(tokens, viewModel.SelectionTokens);
        Assert.AreEqual(selectedContent, viewModel.Editor.ContentText);
        viewModel.SetSelectedCards([]);
        Assert.AreEqual(string.Empty, viewModel.Editor.ContentText);
        Assert.IsNull(viewModel.SelectedCard);
    }

    [TestMethod]
    public void EmptyFilterResult_ClearsSelectionAndEditorAndCanBeReset()
    {
        var service = CreateService();
        using var viewModel = new AiAssistantDebugViewModel(service, null!);
        ShowTopic(viewModel, AiAssistantDebugTopic.SystemMessages);
        Select(viewModel.SystemMessageFilters.Kinds, "ToolDefinition");
        Select(viewModel.SystemMessageFilters.Origins, "Static");
        Assert.IsTrue(viewModel.IsEmpty);
        Assert.AreEqual(0L, viewModel.VisibleSystemMessageTokens);
        Assert.AreEqual(0, viewModel.SelectedCards.Count);
        Assert.AreEqual(string.Empty, viewModel.Editor.ContentText);
        viewModel.SystemMessageFilters.ClearCommand.Execute(null!);
        Assert.IsFalse(viewModel.IsEmpty);
        Assert.IsNotNull(viewModel.SelectedCard);
    }

    [TestMethod]
    public void LiveRefresh_PreservesMultipleSelectedKeysAndUpdatesContentAndTokens()
    {
        var service = CreateService();
        using var viewModel = new AiAssistantDebugViewModel(service, null!);
        ShowTopic(viewModel, AiAssistantDebugTopic.SystemMessages);
        Select(viewModel.SystemMessageFilters.Origins, "Dynamic");
        viewModel.SetSelectedCards(viewModel.Cards.Where(card => card.Key != "fixture:explanation"));
        var history = CreateHistory();
        history.RetainedContents.Single(content => content.Key == "fixture:tool").Content = new string('z', 200);
        service.RestoreSession(history);
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        CollectionAssert.AreEqual(new[] { "fixture:type", "fixture:tool" }, viewModel.SelectedCards.Select(card => card.Key).ToArray());
        Assert.AreEqual(60L, viewModel.SelectedCards.Sum(card => card.EstimatedTokens));
        StringAssert.Contains(viewModel.Editor.ContentText, new string('z', 200));
        Assert.AreEqual(65L, viewModel.VisibleSystemMessageTokens);
    }

    [TestMethod]
    public void Usage_UsesSameUnclippedTokenTotalForAllAndSelection()
    {
        var service = CreateService();
        var history = CreateHistory();
        history.Usage = new AssistantSessionUsage { TotalInputTokens = 3000000000, OutputTokens = 20 };
        service.RestoreSession(history);
        using var viewModel = new AiAssistantDebugViewModel(service, null!);
        Assert.AreEqual(3000000020L, viewModel.Cards.Single().EstimatedTokens);
        Assert.AreEqual(viewModel.Statistics, viewModel.SelectionTokens);
        Assert.AreEqual(viewModel.Cards.Single().Content, viewModel.Editor.ContentText);
    }

    [TestMethod]
    public void HistoryMultiSelection_UsesSameEditorAndSelectionContractAsSystemMessages()
    {
        var service = CreateService();
        using var viewModel = new AiAssistantDebugViewModel(service, null!);
        ShowTopic(viewModel, AiAssistantDebugTopic.History);
        viewModel.SetSelectedCards(viewModel.Cards);
        Assert.AreEqual(2, viewModel.SelectedCards.Count);
        StringAssert.Contains(viewModel.Editor.ContentText, "Fixture user request");
        StringAssert.Contains(viewModel.Editor.ContentText, "Fixture assistant response");
        ShowTopic(viewModel, AiAssistantDebugTopic.Summary);
        Assert.AreEqual(1, viewModel.SelectedCards.Count);
        Assert.AreEqual("Fixture summary", viewModel.Editor.ContentText);
    }

    private static void Select(IReadOnlyList<AiAssistantDebugFilterOption> options, params string[] keys)
    {
        foreach (var option in options)
            option.IsSelected = keys.Contains(option.Key);
    }

    private static void ShowTopic(AiAssistantDebugViewModel viewModel, AiAssistantDebugTopic topic) =>
        viewModel.SelectedTopic = viewModel.Topics.Single(card => card.Topic == topic);

    private static AssistantSystemMessage Message(AssistantSystemMessageKind kind, bool dynamic) =>
        new("fixture", "Fixture", kind, "Fixture content", dynamic);

    private static AiAssistantService CreateService()
    {
        var configuration = new AssistantConfiguration { ApproximateCharactersPerToken = 4 };
        var service = new AiAssistantService(new RecordingAiExecutor(), new EmptyToolApi(), configurationProvider: () => configuration);
        service.RestoreSession(CreateHistory());
        return service;
    }

    private static AiAssistantHistoryDocument CreateHistory() => new()
    {
        ConversationSummary = "Fixture summary",
        RetainedContents =
        [
            new() { Key = "fixture:explanation", Title = "Explanation", Kind = AssistantSystemMessageKind.Explanation, Content = new string('e', 20) },
            new() { Key = "fixture:type", Title = "Type", Kind = AssistantSystemMessageKind.TypeInfo, Content = new string('t', 40) },
            new() { Key = "fixture:tool", Title = "Tool", Kind = AssistantSystemMessageKind.ToolDefinition, Content = new string('a', 80) }
        ],
        SessionMessages =
        [
            new AssistantSingleMessage { MessageRole = "user", Message = "Fixture user request" },
            new AssistantSingleMessage { MessageRole = "assistant", Message = "Fixture assistant response" }
        ]
    };
}
