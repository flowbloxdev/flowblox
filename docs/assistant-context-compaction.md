# AI Assistant summary compaction

The assistant maintains the original conversation history, a summary of older messages,
and recent messages that remain verbatim in its working context.

Summary updates and compaction are separate AI requests:

1. Summarization merges older messages into the existing summary and advances the summarized-message checkpoint.
2. Compaction checks the resulting summary size before the next working request. If it exceeds the configured threshold,
   compaction rewrites only that summary. It does not consume conversation messages or advance the checkpoint.
   Restored summaries can be compacted even when there are no new messages to summarize.

`AssistantConfiguration.MaxSummaryTokens` defaults to **10000** estimated tokens.
`SummaryCompactionTargetRatio` defaults to **0.5**, targeting **5000** tokens at the default threshold.
The target is a fraction of the threshold, not a fraction of the oversized input.
Both settings are independent of `SummaryCompactionRate`, which controls the fraction of older messages merged at once.
Size estimates use `ApproximateCharactersPerToken`; cache savings do not discount the summary compaction threshold.

The compaction request receives the current task only as relevance guidance, not as new content to merge.
Active goals, user constraints, open work, important decisions and necessary identifiers take priority.
Completed work, obsolete intermediate state, repeated facts and readily retrievable schemas receive less space.
Original history messages remain stored unchanged.

During this step the chat displays **Compacting context...** (localized for the UI language).
Compaction makes at most two requests per unchanged summary and settings during a user run.
It retains the smallest nonempty successful result and never cuts text to force the target.
Failures preserve the existing summary; a partial reduction can be retained even if the target remains unmet.
An unmet target is logged, and unchanged content is not repeatedly compacted in subsequent tool rounds of that run.
A later user run can retry. Reaching the target depends on the model; it is not a guaranteed hard limit.

Provider cache savings and the existing context-budget policy are unaffected.
