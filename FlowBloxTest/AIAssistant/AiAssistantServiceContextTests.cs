using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBloxTest.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class AiAssistantServiceContextTests
    {
        [TestMethod]
        public async Task GenerateProjectAsync_IncludesMessagesStagedDuringSummaryInImmediatelyFollowingRequest()
        {
            var executor = new RecordingAiExecutor();
            var service = CreateService(executor, CreateConfiguration(maxLatestMessages: 2, minLatestMessages: 2));
            await service.GenerateProjectAsync("USER-1", CancellationToken.None);
            await service.GenerateProjectAsync("USER-2", CancellationToken.None);

            var runControl = new AssistantRunControl();
            var stagedDuringSummary = false;
            executor.SummaryRequestReceived = _ =>
            {
                Assert.IsTrue(runControl.TryStageUserMessage("INTERMEDIATE-DURING-SUMMARY"));
                stagedDuringSummary = true;
            };

            var result = await service.GenerateProjectAsync("USER-3", CancellationToken.None, runControl);

            Assert.IsTrue(stagedDuringSummary);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, executor.ChatRequests.Count);
            var request = executor.ChatRequests.Last();
            Assert.AreEqual(1, request.Messages.Count(message =>
                message.Role == "user" && message.Content == "INTERMEDIATE-DURING-SUMMARY"));
            var summaryRequest = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantSummary");
            AssertDoesNotContain(summaryRequest.Messages.Single().Content, "INTERMEDIATE-DURING-SUMMARY");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_ContinuesForMessageStagedDuringFinalSummaryRetry()
        {
            var executor = new RecordingAiExecutor();
            var configuration = CreateConfiguration(maxLatestMessages: 2, minLatestMessages: 2);
            configuration.MaxToolRounds = 3;
            var service = CreateService(executor, configuration);
            await service.GenerateProjectAsync("USER-1", CancellationToken.None);
            await service.GenerateProjectAsync("USER-2", CancellationToken.None);

            var runControl = new AssistantRunControl();
            var summaryRequests = 0;
            var messageAccepted = false;
            executor.SummaryRequestReceived = _ =>
            {
                summaryRequests++;
                if (summaryRequests == 1)
                    throw new InvalidOperationException("Simulated initial summary failure.");

                if (summaryRequests == 2)
                    messageAccepted = runControl.TryStageUserMessage("INTERMEDIATE-DURING-FINAL-SUMMARY");
            };

            var result = await service.GenerateProjectAsync("USER-3", CancellationToken.None, runControl);

            Assert.IsTrue(messageAccepted, "Messages must still be accepted during final summarization.");
            Assert.IsTrue(result.Success);
            Assert.AreEqual(4, executor.ChatRequests.Count);
            Assert.AreEqual(1, executor.ChatRequests.Last().Messages.Count(message =>
                message.Role == "user" && message.Content == "INTERMEDIATE-DURING-FINAL-SUMMARY"));
            Assert.IsFalse(runControl.TryStageUserMessage("AFTER-COMPLETION"));
        }

        [TestMethod]
        public async Task GenerateProjectAsync_SummarizesOnlyMessagesLeavingLatestWindow()
        {
            var executor = new RecordingAiExecutor();
            var service = CreateService(executor, CreateConfiguration(maxLatestMessages: 2, minLatestMessages: 2));

            await service.GenerateProjectAsync("USER-1", CancellationToken.None);
            await service.GenerateProjectAsync("USER-2", CancellationToken.None);
            await service.GenerateProjectAsync("USER-3", CancellationToken.None);

            var summaryRequests = executor.Requests
                .Where(x => x.Source == "FlowBloxAIAssistantSummary")
                .ToList();

            Assert.AreEqual(1, summaryRequests.Count);
            AssertContains(summaryRequests[0].Messages.Single().Content, "USER-1");
            AssertContains(summaryRequests[0].Messages.Single().Content, "ASSISTANT-USER-1");
            AssertDoesNotContain(summaryRequests[0].Messages.Single().Content, "USER-2");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_CompactsSummaryByConfiguredRateWhenSummaryIsRequired()
        {
            var executor = new RecordingAiExecutor();
            var service = CreateService(executor, CreateConfiguration(
                maxLatestMessages: 6,
                minLatestMessages: 2,
                summaryCompactionRate: 0.4d));

            await service.GenerateProjectAsync("USER-1", CancellationToken.None);
            await service.GenerateProjectAsync("USER-2", CancellationToken.None);
            await service.GenerateProjectAsync("USER-3", CancellationToken.None);
            await service.GenerateProjectAsync("USER-4", CancellationToken.None);
            await service.GenerateProjectAsync("USER-5", CancellationToken.None);

            var summaryRequests = executor.Requests
                .Where(x => x.Source == "FlowBloxAIAssistantSummary")
                .ToList();

            Assert.AreEqual(1, summaryRequests.Count);
            var summaryContent = summaryRequests[0].Messages.Single().Content;
            AssertContains(summaryContent, "USER-1");
            AssertContains(summaryContent, "ASSISTANT-USER-1");
            AssertContains(summaryContent, "USER-2");
            AssertDoesNotContain(summaryContent, "ASSISTANT-USER-2");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_PreservesAllPriorMessagesThroughSummaryAndLatestWindow()
        {
            var executor = new RecordingAiExecutor();
            var service = CreateService(executor, CreateConfiguration(maxLatestMessages: 2));

            await service.GenerateProjectAsync("USER-1", CancellationToken.None);
            await service.GenerateProjectAsync("USER-2", CancellationToken.None);
            await service.GenerateProjectAsync("USER-3", CancellationToken.None);

            var thirdChatRequest = executor.ChatRequests[2];
            var combinedContext = string.Join(
                "\n",
                thirdChatRequest.SystemMessages.Select(x => x.Content).Concat(thirdChatRequest.Messages.Select(x => x.Content)));

            AssertContains(combinedContext, "USER-1");
            AssertContains(combinedContext, "ASSISTANT-USER-1");
            AssertContains(combinedContext, "USER-2");
            AssertContains(combinedContext, "ASSISTANT-USER-2");
            AssertContains(combinedContext, "USER-3");

            var nonSummaryContext = string.Join(
                "\n",
                thirdChatRequest.Messages
                    .Where(x => !string.Equals(x.Role, "summary", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Content));

            AssertDoesNotContain(nonSummaryContext, "USER-1");
            AssertDoesNotContain(nonSummaryContext, "ASSISTANT-USER-1");
            AssertContains(nonSummaryContext, "USER-2");
            AssertContains(nonSummaryContext, "ASSISTANT-USER-2");
        }

        [TestMethod]
        public void BuildChatRequest_KeepsLatestMessagePairWhenTokenBudgetIsTight()
        {
            var requestResult = AssistantChatRequestBuilder.Build(
                systemPrompt: "S",
                sessionBootstrapPrompt: "B",
                conversationSummary: string.Empty,
                sessionMessages:
                [
                    new AssistantMessagePair
                    {
                        AssistantRequest = "PAIR-ASSISTANT-REQUEST-TOO-LONG-FOR-BUDGET",
                        ToolApiResponse = "PAIR-TOOL-RESPONSE-FITS"
                    }
                ],
                modelPrompt: "C",
                maxLatestMessages: 5,
                minLatestMessages: 1,
                provider: null,
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 14,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 1
                });

            var request = requestResult.Request;
            var historyMessages = request.Messages.Take(request.Messages.Count - 1).ToList();

            Assert.AreEqual(2, historyMessages.Count);
            AssertContains(historyMessages[0].Content, "PAIR-ASSISTANT-REQUEST-TOO-LONG-FOR-BUDGET");
            Assert.AreEqual("assistant", historyMessages[0].Role);
            AssertContains(historyMessages[1].Content, "PAIR-TOOL-RESPONSE-FITS");
            Assert.AreEqual("tool", historyMessages[1].Role);
            Assert.AreEqual("C", request.Messages.Last().Content);
        }

        [TestMethod]
        public void BuildChatRequest_SelectsCompleteSessionMessagesWhenMaxLatestMessagesIsReached()
        {
            var requestResult = AssistantChatRequestBuilder.Build(
                systemPrompt: "S",
                sessionBootstrapPrompt: "B",
                conversationSummary: string.Empty,
                sessionMessages:
                [
                    new AssistantSingleMessage { MessageRole = "user", Message = "SINGLE-1" },
                    new AssistantMessagePair { AssistantRequest = "PAIR-2-REQUEST", ToolApiResponse = "PAIR-2-RESPONSE" },
                    new AssistantMessagePair { AssistantRequest = "PAIR-3-REQUEST", ToolApiResponse = "PAIR-3-RESPONSE" }
                ],
                modelPrompt: "C",
                maxLatestMessages: 2,
                minLatestMessages: 1,
                provider: null,
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 1000,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 4
                });

            var historyMessages = requestResult.Request.Messages.Take(requestResult.Request.Messages.Count - 1).ToList();

            Assert.AreEqual(4, historyMessages.Count);
            AssertContains(historyMessages[0].Content, "PAIR-2-REQUEST");
            AssertContains(historyMessages[1].Content, "PAIR-2-RESPONSE");
            AssertContains(historyMessages[2].Content, "PAIR-3-REQUEST");
            AssertContains(historyMessages[3].Content, "PAIR-3-RESPONSE");
            Assert.AreEqual(1, requestResult.FirstIncludedHistoryMessageIndex);
        }

        [TestMethod]
        public void BuildChatRequest_ExcludesAlreadySummarizedSessionMessages()
        {
            var requestResult = AssistantChatRequestBuilder.Build(
                systemPrompt: "S",
                sessionBootstrapPrompt: "B",
                conversationSummary: "Summary contains earlier context",
                sessionMessages:
                [
                    new AssistantSingleMessage { MessageRole = "user", Message = "OLD-USER" },
                    new AssistantMessagePair { AssistantRequest = "OLD-REQUEST", ToolApiResponse = "OLD-RESPONSE" },
                    new AssistantMessagePair { AssistantRequest = "CURRENT-REQUEST", ToolApiResponse = "CURRENT-RESPONSE" }
                ],
                modelPrompt: "C",
                maxLatestMessages: 5,
                minLatestMessages: 1,
                provider: null,
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 1000,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 4
                },
                summarizedMessageCount: 2);

            var context = string.Join("\n", requestResult.Request.Messages.Select(x => x.Content));

            AssertContains(context, "Summary contains earlier context");
            AssertDoesNotContain(context, "OLD-USER");
            AssertDoesNotContain(context, "OLD-REQUEST");
            AssertDoesNotContain(context, "OLD-RESPONSE");
            AssertContains(context, "CURRENT-REQUEST");
            AssertContains(context, "CURRENT-RESPONSE");
            Assert.AreEqual(2, requestResult.FirstIncludedHistoryMessageIndex);
        }

        [TestMethod]
        public void BuildChatRequest_MarksStableSystemMessagesAndSummaryForCaching()
        {
            var requestResult = AssistantChatRequestBuilder.Build(
                systemPrompt: "Stable system",
                sessionBootstrapPrompt: "Stable bootstrap",
                conversationSummary: "Variable summary",
                sessionMessages: [],
                modelPrompt: "Current prompt",
                maxLatestMessages: 5,
                minLatestMessages: 1,
                provider: null,
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 1000,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 4
                });

            var request = requestResult.Request;

            Assert.AreEqual(2, request.SystemMessages.Count);
            Assert.AreEqual(AIChatCacheBehavior.PreferCache, request.SystemMessages[0].CacheBehavior);
            Assert.AreEqual(AIChatCacheBehavior.PreferCache, request.SystemMessages[1].CacheBehavior);
            Assert.AreEqual("summary", request.Messages[0].Role);
            AssertContains(request.Messages[0].Content, "Conversation Summary:");
            Assert.AreEqual(AIChatCacheBehavior.PreferCache, request.Messages[0].CacheBehavior);
            Assert.AreEqual(AuthorRole.User, request.Messages[0].SemanticContent?.Role);
        }

        [TestMethod]
        public void BuildChatRequest_ExpandsMessagePairsToNativeFunctionMessages()
        {
            var assistantRequest = """
            {
              "assistantMessage": "Reading project",
              "final": false,
              "toolCalls": [
                { "toolName": "GetProjectJson", "arguments": { "includeLayout": true } }
              ]
            }
            """;
            var toolApiResponse =
                "Tool execution updates since last assistant request:\n" +
                "{\"tool\":\"GetProjectJson\",\"arguments\":{\"includeLayout\":true},\"response\":{\"ok\":true,\"result\":{\"project\":\"Demo\"},\"error\":\"\",\"log\":[]}}";

            var requestResult = AssistantChatRequestBuilder.Build(
                systemPrompt: "S",
                sessionBootstrapPrompt: "B",
                conversationSummary: "Existing summary",
                sessionMessages:
                [
                    new AssistantMessagePair
                    {
                        AssistantRequest = assistantRequest,
                        ToolApiResponse = toolApiResponse
                    }
                ],
                modelPrompt: "C",
                maxLatestMessages: 5,
                minLatestMessages: 1,
                provider: new MetadataMappingProvider(),
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 1000,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 4
                });

            var messages = requestResult.Request.Messages;

            Assert.AreEqual("summary", messages[0].Role);
            AssertContains(messages[0].Content, "Existing summary");
            Assert.AreEqual(AIChatCacheBehavior.PreferCache, messages[0].CacheBehavior);
            Assert.AreEqual(AuthorRole.User, messages[0].SemanticContent?.Role);

            Assert.AreEqual("assistant", messages[1].Role);
            Assert.AreEqual(AuthorRole.Assistant, messages[1].SemanticContent?.Role);
            var functionCall = messages[1].SemanticContent?.Items.OfType<FunctionCallContent>().Single();
            Assert.IsNotNull(functionCall);
            Assert.AreEqual("GetProjectJson", functionCall.FunctionName);

            Assert.AreEqual("tool", messages[2].Role);
            Assert.AreEqual(AuthorRole.Tool, messages[2].SemanticContent?.Role);
            var functionResult = messages[2].SemanticContent?.Items.OfType<FunctionResultContent>().Single();
            Assert.IsNotNull(functionResult);
            Assert.AreEqual(functionCall.Id, functionResult.CallId);
            AssertContains(functionResult.Result?.ToString() ?? string.Empty, "\"project\":\"Demo\"");
        }

        [TestMethod]
        public void BuildChatRequest_MapsStoredMessageMetadataWithProvider()
        {
            var assistantRequest = """
            {
              "assistantMessage": "Reading project",
              "final": false,
              "toolCalls": [
                { "toolName": "GetProjectJson", "arguments": { "includeLayout": true } }
              ]
            }
            """;

            var requestResult = AssistantChatRequestBuilder.Build(
                systemPrompt: "S",
                sessionBootstrapPrompt: "B",
                conversationSummary: string.Empty,
                sessionMessages:
                [
                    new AssistantMessagePair
                    {
                        AssistantRequest = assistantRequest,
                        ToolApiResponse = "{\"response\":{\"ok\":true}}",
                        Metadata = new AIChatMessageMetadata
                        {
                            Values =
                            {
                                ["providerSpecificValue"] = "OPAQUE_VALUE"
                            }
                        }
                    }
                ],
                modelPrompt: "C",
                maxLatestMessages: 5,
                minLatestMessages: 1,
                provider: new MetadataMappingProvider(),
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 1000,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 4
                });

            var assistantMessage = requestResult.Request.Messages.Single(x => x.Role == "assistant");

            Assert.IsNotNull(assistantMessage.SemanticContent?.Metadata);
            Assert.AreEqual("OPAQUE_VALUE", assistantMessage.SemanticContent!.Metadata!["ProviderSpecificKey"]);
        }

        [TestMethod]
        public void HistoryDocument_RoundTripsMessagePairMetadata()
        {
            var history = new AiAssistantHistoryDocument
            {
                SessionMessages =
                [
                    new AssistantMessagePair
                    {
                        AssistantRequest = "ASSISTANT-REQUEST",
                        ToolApiResponse = "TOOL-RESPONSE",
                        Metadata = new AIChatMessageMetadata
                        {
                            Values =
                            {
                                ["providerSpecificValue"] = "OPAQUE_VALUE"
                            }
                        }
                    }
                ]
            };

            var json = JsonConvert.SerializeObject(history);
            var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(json);

            var pair = AssertIsInstanceOfType<AssistantMessagePair>(restored!.SessionMessages.Single());
            Assert.AreEqual("OPAQUE_VALUE", pair.Metadata.Values["providerSpecificValue"]);
        }

        [TestMethod]
        public async Task GenerateProjectAsync_CompactsHistoryAndSendsNativeSemanticKernelToolMessages()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();

            var getBaseFlowBlockRequest = BuildToolCallResponse(
                "Inspect base flow block",
                "GetTypeKindsInfo",
                new JObject
                {
                    ["typeFullName"] = "FlowBlox.Core.Models.FlowBlocks.Base.BaseFlowBlock"
                });
            var createStartRequest = BuildToolCallResponse(
                "Create start",
                "CreateFlowBlock",
                new JObject
                {
                    ["typeFullName"] = "FlowBlox.Core.Models.FlowBlocks.SequenceFlow.StartFlowBlock",
                    ["name"] = "Start"
                });
            var getBaseResultFlowBlockRequest = BuildToolCallResponse(
                "Inspect base result flow block",
                "GetTypeKindsInfo",
                new JObject
                {
                    ["typeFullName"] = "FlowBlox.Core.Models.FlowBlocks.Base.BaseResultFlowBlock"
                });
            var createNodeRequest = BuildToolCallResponse(
                "Create node",
                "CreateFlowBlock",
                new JObject
                {
                    ["typeFullName"] = "FlowBlox.Core.Models.FlowBlocks.SequenceFlow.NodeFlowBlock",
                    ["name"] = "Node"
                });

            var provider = new RecordingOpenAICompatibleProvider(
                getBaseFlowBlockRequest,
                createStartRequest,
                getBaseResultFlowBlockRequest,
                createNodeRequest,
                "{\"assistantMessage\":\"Done.\",\"final\":true,\"toolCalls\":[]}");
            var toolApi = new SizedToolApi();
            var config = CreateConfiguration(maxLatestMessages: 50, minLatestMessages: 1, summaryCompactionRate: 1d);
            provider.Name = "RecordingProvider";
            config.Providers.Add(provider);
            config.MaxToolRounds = 5;
            config.ApproximateCharactersPerToken = 1;
            config.MaxContextTokens = AiAssistantService.ContinuationModelPrompt.Length + SizedToolApi.TypeInfoResponseLength + 50;

            var service = CreateService(new AiProviderExecutor(), toolApi, config);

            var result = await service.GenerateProjectAsync("USER-MESSAGE-1", CancellationToken.None);

            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.IsTrue(provider.SummaryRequestCount > 0);

            var postSummaryChat = provider.NormalChatHistories
                .First(history => history.Any(IsSummaryMessage));
            var summaryMessage = postSummaryChat.First(IsSummaryMessage);
            Assert.AreEqual(AuthorRole.User, summaryMessage.Role);
            AssertContains(summaryMessage.Content ?? string.Empty, "SUMMARY-");

            var postSummaryHistoryMessages = postSummaryChat
                .Where(IsAssistantOrToolHistoryMessage)
                .ToList();
            Assert.AreEqual(2, postSummaryHistoryMessages.Count);
            Assert.AreEqual(AuthorRole.Assistant, postSummaryHistoryMessages[0].Role);
            Assert.AreEqual(AuthorRole.Tool, postSummaryHistoryMessages[1].Role);

            var functionCall = postSummaryHistoryMessages[0].Items.OfType<FunctionCallContent>().Single();
            var functionResult = postSummaryHistoryMessages[1].Items.OfType<FunctionResultContent>().Single();
            Assert.AreEqual(functionCall.Id, functionResult.CallId);

            var finalChat = provider.NormalChatHistories.Last();
            var finalHistoryMessages = finalChat
                .Where(IsAssistantOrToolHistoryMessage)
                .ToList();
            Assert.AreEqual(2, finalHistoryMessages.Count);

            var latestFunctionCall = finalHistoryMessages[0].Items.OfType<FunctionCallContent>().Single();
            var latestFunctionResult = finalHistoryMessages[1].Items.OfType<FunctionResultContent>().Single();
            Assert.AreEqual("CreateFlowBlock", latestFunctionCall.FunctionName);
            Assert.IsNotNull(latestFunctionCall.Arguments);
            AssertContains(latestFunctionCall.Arguments!["typeFullName"]?.ToString() ?? string.Empty, "NodeFlowBlock");
            Assert.AreEqual(latestFunctionCall.Id, latestFunctionResult.CallId);
        }


        [TestMethod]
        public async Task GenerateProjectAsync_SummarizesMessagesExcludedByEffectiveTokenWindow()
        {
            var executor = new RecordingAiExecutor();
            var config = CreateConfiguration(maxLatestMessages: 4);
            config.MinLatestMessages = 1;
            config.MaxContextTokens = 35;
            config.ApproximateCharactersPerToken = 1;
            var service = CreateService(executor, config);

            await service.GenerateProjectAsync("USER-1-LONG", CancellationToken.None);
            await service.GenerateProjectAsync("USER-2-LONG", CancellationToken.None);
            await service.GenerateProjectAsync("USER-3-LONG", CancellationToken.None);

            var thirdChatRequest = executor.ChatRequests[2];
            var combinedContext = string.Join(
                "\n",
                thirdChatRequest.SystemMessages.Select(x => x.Content).Concat(thirdChatRequest.Messages.Select(x => x.Content)));

            AssertContains(combinedContext, "USER-1-LONG");
            AssertContains(combinedContext, "ASSISTANT-USER-1-LONG");
            AssertContains(combinedContext, "USER-2-LONG");
            AssertContains(combinedContext, "ASSISTANT-USER-2-LONG");
            AssertContains(combinedContext, "USER-3-LONG");
        }

        [TestMethod]
        public void BuildSummaryRequest_PreservesExistingSummaryAndMessagePairs()
        {
            var request = AssistantSummaryRequestBuilder.Build(
                "## Goals\n- Existing goal",
                [new AssistantMessagePair { AssistantRequest = "ASSISTANT-REQUEST-1", ToolApiResponse = "TOOL-RESPONSE-1" }]);

            AssertContains(request.Messages.Single().Content, "Existing goal");
            AssertContains(request.Messages.Single().Content, "ASSISTANT-REQUEST-1");
            AssertContains(request.Messages.Single().Content, "TOOL-RESPONSE-1");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_PersistsTechnicalSessionMessagesIncludingToolRounds()
        {
            var executor = new QueuedAiExecutor(
                "{\"assistantMessage\":\"Reading project details.\",\"final\":false,\"toolCalls\":[{\"toolName\":\"GetProjectJson\",\"arguments\":{}}]}",
                "{\"assistantMessage\":\"Done.\",\"final\":true,\"toolCalls\":[]}");
            var config = CreateConfiguration(maxLatestMessages: 10);
            config.MaxToolRounds = 2;
            var service = CreateService(executor, config);

            await service.GenerateProjectAsync("USER-TOOL-ROUND", CancellationToken.None);

            var history = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(history);

            Assert.AreEqual(3, history.SessionMessages.Count);
            var initialMessage = AssertIsInstanceOfType<AssistantSingleMessage>(history.SessionMessages[0]);
            Assert.AreEqual("user", initialMessage.Role);
            AssertContains(initialMessage.CompleteMessage, "User prompt:");
            AssertContains(initialMessage.CompleteMessage, "USER-TOOL-ROUND");

            var pair = AssertIsInstanceOfType<AssistantMessagePair>(history.SessionMessages[1]);
            AssertContains(pair.AssistantRequest, "GetProjectJson");
            AssertContains(pair.ToolApiResponse, "Tool execution updates since last assistant request:");
            AssertContains(pair.ToolApiResponse, "GetProjectJson");

            var finalMessage = AssertIsInstanceOfType<AssistantSingleMessage>(history.SessionMessages[2]);
            Assert.AreEqual("assistant", finalMessage.Role);
            AssertContains(finalMessage.CompleteMessage, "Done.");
        }

        [TestMethod]
        public void CurrentConversationSummary_ReturnsSummaryFromRestoredSession()
        {
            var service = CreateService(new RecordingAiExecutor(), CreateConfiguration(maxLatestMessages: 10));
            var summaryChangedCount = 0;
            service.ConversationSummaryChanged += (_, _) => summaryChangedCount++;
            service.RestoreSession(new AiAssistantHistoryDocument
            {
                ConversationSummary = "CURRENT-SUMMARY"
            });

            Assert.AreEqual("CURRENT-SUMMARY", service.CurrentConversationSummary);
            Assert.AreEqual(1, summaryChangedCount);

            service.ResetSession();

            Assert.AreEqual(string.Empty, service.CurrentConversationSummary);
            Assert.AreEqual(2, summaryChangedCount);
        }

        [TestMethod]
        public async Task RestoreSession_UsesPersistedSessionMessagesBeforeTranscriptFallback()
        {
            var executor = new RecordingAiExecutor();
            var service = CreateService(executor, CreateConfiguration(maxLatestMessages: 10));
            service.RestoreSession(new AiAssistantHistoryDocument
            {
                SessionMessages =
                [
                    new AssistantSingleMessage { MessageRole = "user", Message = "TECHNICAL-ROUND-PROMPT" },
                    new AssistantSingleMessage { MessageRole = "assistant", Message = "TECHNICAL-ASSISTANT-OUTPUT" }
                ],
                Transcripts =
                [
                    new AssistantTranscriptLine { Kind = AssistantTranscriptKind.User, Text = "UI-ONLY-USER" },
                    new AssistantTranscriptLine { Kind = AssistantTranscriptKind.Assistant, Text = "UI-ONLY-ASSISTANT" }
                ]
            });

            await service.GenerateProjectAsync("NEXT-USER", CancellationToken.None);

            var combinedContext = string.Join(
                "\n",
                executor.ChatRequests.Single().Messages.Select(x => x.Content));

            AssertContains(combinedContext, "TECHNICAL-ROUND-PROMPT");
            AssertContains(combinedContext, "TECHNICAL-ASSISTANT-OUTPUT");
            AssertDoesNotContain(combinedContext, "UI-ONLY-USER");
            AssertDoesNotContain(combinedContext, "UI-ONLY-ASSISTANT");
        }

        [TestMethod]
        public void HistoryDocument_RoundTripsPolymorphicSessionMessages()
        {
            var history = new AiAssistantHistoryDocument
            {
                SessionMessages =
                [
                    new AssistantSingleMessage { MessageRole = "user", Message = "USER-SINGLE" },
                    new AssistantMessagePair { AssistantRequest = "ASSISTANT-REQUEST", ToolApiResponse = "TOOL-RESPONSE" },
                    new AssistantSingleMessage { MessageRole = "assistant", Message = "ASSISTANT-FINAL" }
                ]
            };

            var json = JsonConvert.SerializeObject(history);
            var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(3, restored!.SessionMessages.Count);
            AssertIsInstanceOfType<AssistantSingleMessage>(restored.SessionMessages[0]);
            AssertIsInstanceOfType<AssistantMessagePair>(restored.SessionMessages[1]);
            AssertIsInstanceOfType<AssistantSingleMessage>(restored.SessionMessages[2]);
            AssertContains(restored.SessionMessages[1].CompleteMessage, "ASSISTANT-REQUEST");
            AssertContains(restored.SessionMessages[1].CompleteMessage, "TOOL-RESPONSE");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_PersistsAssistantToolRequestWhenToolProcessingIsCanceled()
        {
            var executor = new QueuedAiExecutor(
                "{\"assistantMessage\":\"I will update the flow.\",\"final\":false,\"toolCalls\":[{\"toolName\":\"CancelingTool\",\"arguments\":{}}]}");
            var service = CreateService(
                executor,
                new CancelingToolApi(),
                CreateConfiguration(maxLatestMessages: 10));

            try
            {
                await service.GenerateProjectAsync("USER-CANCEL", CancellationToken.None);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }

            var history = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(history);

            Assert.AreEqual(2, history.SessionMessages.Count);
            AssertIsInstanceOfType<AssistantSingleMessage>(history.SessionMessages[0]);
            var assistantMessage = AssertIsInstanceOfType<AssistantSingleMessage>(history.SessionMessages[1]);
            Assert.AreEqual("assistant", assistantMessage.Role);
            AssertContains(assistantMessage.CompleteMessage, "CancelingTool");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_PersistsNormalizedInstructionInsteadOfRawAssistantOutput()
        {
            var executor = new QueuedAiExecutor(
                "preface that should not be persisted {\"assistantMessage\":\"Reading project details.\",\"final\":false,\"toolCalls\":[{\"toolName\":\"GetProjectJson\",\"arguments\":{}}]} trailing text",
                "{\"assistantMessage\":\"Done.\",\"final\":true,\"toolCalls\":[]}");
            var config = CreateConfiguration(maxLatestMessages: 10);
            config.MaxToolRounds = 2;
            var service = CreateService(executor, config);

            await service.GenerateProjectAsync("USER-NORMALIZED", CancellationToken.None);

            var history = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(history);

            var pair = AssertIsInstanceOfType<AssistantMessagePair>(history.SessionMessages[1]);
            AssertContains(pair.AssistantRequest, "GetProjectJson");
            AssertDoesNotContain(pair.AssistantRequest, "preface that should not be persisted");
            AssertDoesNotContain(pair.AssistantRequest, "trailing text");
        }

        [TestMethod]
        public void AssistantInstructionParser_CapturesJsonParseExceptionAndResponseContent()
        {
            var parser = new AiResponseInstructionParserService();
            const string output = "{\"assistantMessage\":\"Broken\",\"final\":false,\"toolCalls\":[}";

            var result = parser.Parse(output);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Exception);
            AssertIsInstanceOfType<Newtonsoft.Json.JsonReaderException>(result.Exception);
            Assert.AreEqual(output, result.ResponseContent);
        }

        [TestMethod]
        public void AssistantInstructionParser_ReturnsFormatExceptionWhenNoJsonObjectStartExists()
        {
            var parser = new AiResponseInstructionParserService();
            const string output = "plain assistant text without a JSON object";

            var result = parser.ParseFirstJsonObject(output);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Exception);
            var exception = AssertIsInstanceOfType<FormatException>(result.Exception);
            Assert.AreEqual("Assistant response did not contain a JSON object.", exception.Message);
            Assert.AreEqual(output, result.ResponseContent);
        }

        [TestMethod]
        public async Task GenerateProjectAsync_AddsFormatFailureDetailsToRetryPrompt()
        {
            var executor = new QueuedAiExecutor(
                "{\"assistantMessage\":\"Broken\",\"final\":false,\"toolCalls\":[}",
                "{\"assistantMessage\":\"Recovered.\",\"final\":true,\"toolCalls\":[]}");
            var config = CreateConfiguration(maxLatestMessages: 10);
            config.MaxToolRounds = 2;
            var service = CreateService(executor, config);

            var result = await service.GenerateProjectAsync("USER-FORMAT-RETRY", CancellationToken.None);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, executor.ChatRequests.Count);
            var retryPrompt = executor.ChatRequests[1].Messages.Last().Content;
            AssertContains(retryPrompt, "JsonReaderException");
            AssertContains(retryPrompt, "{\"assistantMessage\":\"Broken\"");
        }

        [TestMethod]
        public async Task GenerateProjectAsync_LoadedExplanationBecomesVisibleInDebuggerAndNextRequest()
        {
            var retrieve = "{\"assistantMessage\":\"Read guidance.\",\"final\":false,\"toolCalls\":[{\"toolName\":\"LoadExplanation\",\"arguments\":{\"key\":\"explaining_generators\"}}]}";
            var executor = new QueuedAiExecutor(
                retrieve,
                "{\"assistantMessage\":\"Done.\",\"final\":true,\"toolCalls\":[]}");
            var config = CreateConfiguration(maxLatestMessages: 10);
            config.MaxToolRounds = 3;
            var service = CreateService(
                executor,
                new DefaultToolApi(),
                config);
            var snapshots = new List<AssistantDebugSnapshot>();
            service.DebugStateChanged += (_, _) => snapshots.Add(service.GetDebugSnapshot());
            var result = await service.GenerateProjectAsync("READ-GUIDANCE", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.AreEqual(2, executor.ChatRequests[0].SystemMessages.Count);
            Assert.AreEqual(3, executor.ChatRequests[1].SystemMessages.Count);
            Assert.AreEqual(AIChatCacheBehavior.PreferCache, executor.ChatRequests[1].SystemMessages.Last().CacheBehavior);
            var snapshot = service.GetDebugSnapshot();
            Assert.AreEqual(1, snapshot.SystemMessages.Count(x => x.IsRuntimeBound));
            Assert.AreEqual(3, snapshot.History.Count);
            var loaded = (AssistantMessagePair)snapshot.History[1].Message;
            Assert.IsTrue(snapshots.Any(x => x.SystemMessages.Any(m => m.IsRuntimeBound)));
            loaded.AssistantRequest = "MUTATED-SNAPSHOT";
            AssertDoesNotContain(service.GetDebugSnapshot().History[1].Message.CompleteMessage, "MUTATED-SNAPSHOT");
            var saved = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(saved);
            service.RestoreSession(saved);
            Assert.AreEqual(0, service.GetDebugSnapshot().SystemMessages.Count(x => x.IsRuntimeBound));
            Assert.AreEqual(3, service.GetDebugSnapshot().History.Count);
            service.ResetSession();
            Assert.AreEqual(0, snapshots.Last().History.Count);
        }

        private static AiAssistantService CreateService(RecordingAiExecutor executor, AssistantConfiguration configuration)
        {
            return new AiAssistantService(
                executor,
                new EmptyToolApi(),
                configurationProvider: () => configuration);
        }

        private static AiAssistantService CreateService(IAiExecutor executor, AssistantConfiguration configuration)
        {
            return new AiAssistantService(
                executor,
                new EmptyToolApi(),
                configurationProvider: () => configuration);
        }

        private static AiAssistantService CreateService(
            IAiExecutor executor,
            IFlowBloxAIToolApi toolApi,
            AssistantConfiguration configuration)
        {
            return new AiAssistantService(
                executor,
                toolApi,
                configurationProvider: () => configuration);
        }

        private static AssistantConfiguration CreateConfiguration(
            int maxLatestMessages,
            int minLatestMessages = 1,
            double summaryCompactionRate = 0.4d)
        {
            return new AssistantConfiguration
            {
                MaxToolRounds = 1,
                MaxLatestMessages = maxLatestMessages,
                MaxContextTokens = 100000,
                ReservedResponseTokens = 0,
                ApproximateCharactersPerToken = 4,
                MinLatestMessages = minLatestMessages,
                SummaryCompactionRate = summaryCompactionRate,
                EnableAutomaticAdjustment = false
            };
        }

        private static void AssertContains(string actual, string expected)
        {
            StringAssert.Contains(actual, expected);
        }

        private static void AssertDoesNotContain(string actual, string unexpected)
        {
            Assert.IsFalse(actual.Contains(unexpected, StringComparison.Ordinal), $"Did not expect '{unexpected}' in:\n{actual}");
        }

        private static string BuildToolCallResponse(string assistantMessage, string toolName, JObject arguments)
        {
            return new JObject
            {
                ["assistantMessage"] = assistantMessage,
                ["final"] = false,
                ["toolCalls"] = new JArray
                {
                    new JObject
                    {
                        ["toolName"] = toolName,
                        ["arguments"] = arguments
                    }
                }
            }.ToString(Formatting.None);
        }

        private static bool IsSummaryMessage(ChatMessageContent message)
        {
            return message.Role == AuthorRole.User &&
                   (message.Content ?? string.Empty).Contains("SUMMARY-", StringComparison.Ordinal);
        }

        private static bool IsAssistantOrToolHistoryMessage(ChatMessageContent message)
        {
            return message.Role == AuthorRole.Assistant || message.Role == AuthorRole.Tool;
        }

        private static T AssertIsInstanceOfType<T>(object value)
        {
            Assert.IsInstanceOfType(value, typeof(T));
            return (T)value;
        }

        private sealed class MetadataMappingProvider : AIProviderBase
        {
            public override string ProviderType => "MetadataMappingTest";

            public override IReadOnlyDictionary<string, object>? BuildChatMessageMetadata(AIChatMessageMetadata? metadata)
            {
                if (metadata?.Values.TryGetValue("providerSpecificValue", out var value) != true)
                    return null;

                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["ProviderSpecificKey"] = value
                };
            }

            protected override Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
