using System.Text;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using Newtonsoft.Json;

namespace FlowBlox.AIAssistant.Builder
{
    internal static class AssistantSummaryRequestBuilder
    {
        public static AIChatRequest Build(
            string currentSummary,
            IReadOnlyList<AssistantSessionMessage> messagesToSummarize,
            IReadOnlyList<AssistantRetainedContent>? retainedSystemContents = null,
            AIChatRequest? prefix = null)
        {
            var request = new AIChatRequest
            {
                Source = "FlowBloxAIAssistantSummary"
            };

            request.SystemMessages.Add(new AIChatMessage
            {
                Role = "system",
                Content = SystemInstruction
            });

            var retainedSystemKeys = (retainedSystemContents ?? [])
                .Where(content => !string.IsNullOrWhiteSpace(content.Key) && !string.IsNullOrWhiteSpace(content.Content))
                .Select(content => content.Key)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("Authoritative retained-system-content inventory (keys available in the working Assistant's next system context):");
            sb.AppendLine(JsonConvert.SerializeObject(retainedSystemKeys));
            sb.AppendLine();
            if (prefix != null)
            {
                sb.AppendLine("Merge the preceding conversation messages into its existing Conversation Summary, if present. " +
                    "The protected latest messages are outside this prefix and must not be summarized.");
                foreach (var pair in messagesToSummarize.OfType<AssistantMessagePair>().Where(pair => pair.CompactedToolResults.Count > 0))
                {
                    sb.AppendLine("Original source for previously compacted tool results (same history, not new work):");
                    sb.AppendLine(pair.CompleteMessage);
                }
                request.Messages.Add(new AIChatMessage { Role = "user", Content = sb.ToString().TrimEnd() });
                return AssistantMaintenanceRequestBuilder.Append(prefix, request);
            }
            sb.AppendLine("Current structured summary:");
            sb.AppendLine(string.IsNullOrWhiteSpace(currentSummary) 
                ? "(empty)" 
                : currentSummary.Trim());
            sb.AppendLine();
            sb.AppendLine("New messages to merge into the summary:");

            foreach (var message in messagesToSummarize ?? Array.Empty<AssistantSessionMessage>())
            {
                if (string.IsNullOrWhiteSpace(message?.CompleteMessage))
                    continue;

                sb.AppendLine();
                sb.AppendLine(message switch
                {
                    AssistantMessagePair => "MessagePair (AssistantRequest + ToolApiResponse):",
                    AssistantSingleMessage single when string.Equals(single.Role, "assistant", StringComparison.OrdinalIgnoreCase) => "SingleMessage (Assistant):",
                    _ => "SingleMessage (User):"
                });
                sb.AppendLine(message.CompleteMessage.Trim());
            }

            request.Messages.Add(new AIChatMessage
            {
                Role = "user",
                Content = sb.ToString().TrimEnd()
            });

            return request;
        }

        private const string SystemInstruction = """
            Maintain a durable Structured Historical Memory for the FlowBlox AI Assistant conversation.
            - Merge only the provided new messages into the current summary and return the complete updated memory.
            - The authoritative retained-system-content inventory lists full schemas, explanations and tool contracts already retained or
              guaranteed to be retained as system messages before the working Assistant's next request. Their original payloads may remain
              in history for prefix-cache reuse. Do not duplicate those payloads in the summary; preserve task-specific decisions and diagnostics.
            - If the current summary already duplicates static content covered by that inventory, replace the duplicated description
              with a brief retainedContentKey reference. Keep conversation-specific findings, changes, outcomes, and errors.
            - A retainedContentKey in that inventory means available content, not a failed lookup, missing schema, inaccessible payload,
              or open retrieval task. Correct previous missing-content/blocker claims resolved by this inventory; preserve unrelated failures.
            - A tag or reference whose key is absent from the inventory does not prove system availability. Preserve relevant facts from
              the supplied original results instead of assuming that their content has been retained elsewhere.
            - If the current summary uses another schema, migrate all relevant information without intentionally losing project history,
              rationale, current state, reusable knowledge, or active diagnostics.
            - Use these Markdown sections: Goals And Constraints, Current Project State, Decision And Change History, Open Work, User
              Preferences, Reusable FlowBlox And Tool Knowledge, Active Diagnostics.
            - If a skill project is still being edited, also maintain Active Skill Project with Name, Purpose, Input Fields, and Output Fields;
              remove that section only when no skill project is active.
            - Current Project State is authoritative for exact current object names, types, connections, property values, test definitions,
              generators, and ManagedObjects, including configured AI provider objects and their links.
            - Decision And Change History preserves chronology, alternatives, failures, corrections, rationale, and outcomes so later questions
              about why a FlowBlock was configured in a particular way can be answered. Do not replace useful history with only the final state.
            - Reusable FlowBlox And Tool Knowledge preserves important learned behavior of FlowBlocks, properties, types, tests, generators,
              ManagedObjects, and tools even when it could potentially be queried again. Compact repeated schemas and raw output, but retain
              their useful facts.
            - Active Diagnostics preserves every identifier and detail still needed for ongoing investigation, including active run IDs,
              FieldChange IDs, generated-result IDs, dataset indexes, relevant errors, and the next diagnostic step.
            - Remove a diagnostic identifier only after its investigation is resolved and the identifier is no longer needed; preserve the
              resulting finding, reasoning, and outcome in history or reusable knowledge.
            - Apply the DRY principle throughout the memory: keep one canonical representation of each fact, use compact references instead of
              restating unchanged details, and do not repeat information merely to make every section self-contained.
            - Generally store an exact current value in Current Project State, its reason and evolution in Decision And Change History, and only
              generally reusable behavior in Reusable FlowBlox And Tool Knowledge. Cross-section repetition is allowed only when it contributes
              distinct current-state, historical, or reusable meaning.
            - Preserve exact FlowBlox identifiers, property/update and collection paths, enum values, placeholders, resolver syntax and values,
              connection directions, selection constraints, user preferences, and relevant ManagedObject constraints whenever they may be needed
              to resume work.
            - Minimize token usage and request traffic while preserving informational content, recognizable history, rationale, active working
              memory, and explainability. Generic bootstrap guidance may be omitted only when it adds no conversation-specific fact or learned
              behavior. Prefer concise grouped bullets, but completeness and explainability take precedence over brevity. Write '(none)' for
              empty sections.
            """;
    }
}
