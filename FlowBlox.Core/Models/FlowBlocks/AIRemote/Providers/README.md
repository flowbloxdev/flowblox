# AI provider prompt caching

For the complete run/round pipeline and summary/compaction lifecycle, see the
[AI Assistant processing guide](../../../../../FlowBlox.AIAssistant/Services/README.md).

FlowBlox uses the maximum supported prompt-prefix caching by default. Provider and
message callers do not opt in through a `PreferCache` flag. Cache activation and
breakpoint placement belong to each provider implementation, including new providers.

Prompt caching reuses the provider's processing of an unchanged input prefix. Every
request still sends its complete required context; FlowBlox does not reuse an earlier
model answer or omit context merely because it was sent before. Cache hits depend on
the provider, model, minimum prefix length, and cache lifetime.

## Current provider behavior

- OpenAI and DeepSeek handle prefix caching automatically. Their implementations use
  [OpenAICompatibleProviderBase](../Base/OpenAICompatibleProviderBase.cs) without an
  additional cache activation setting.
- [AnthropicAIProvider](AnthropicAIProvider.cs) enables fine-grained prompt caching
  and sets explicit `ephemeral` breakpoints when preparing the provider request:
  - At the stable bootstrap boundary, when supplied by the Assistant's prompt layout.
  - At the last nonempty system block.
  - At an optional reusable conversation boundary before protected latest messages or a maintenance suffix.
  - At the last conversation content block, including native tool calls and results.
- Coincident system boundaries receive one marker. The current Anthropic strategy
  uses at most four breakpoints, within the API's four-breakpoint limit. System
  content is cacheable by default even for requests outside the Assistant.
- Providers with an OpenAI-compatible API do not necessarily share identical cache
  capabilities. Check the actual provider's support when adding or changing one.

`AIChatMessage.IsBootstrapCacheBoundary` and `IsContextCacheBoundary` describe reusable
prompt boundaries for all providers. They are not caching enable/disable flags.
Anthropic translates them into its own API markers. Other providers need not interpret them.

## Assistant prompt layout and retained tool information

The [Assistant request builder](../../../../../FlowBlox.AIAssistant/Builder/AssistantChatRequestBuilder.cs)
places content in this order:

1. Fixed system instructions and session bootstrap, including applicable initial guidance.
2. One ordered sequence of dynamically retained explanations, type information, and tool contracts.
3. Conversation summary, then the selected recent history.
4. Current user or continuation prompt.

Keep the earliest content stable across requests. Changing an earlier prefix prevents
reuse of cached processing after that change, even if later text remains identical.
The separate Anthropic bootstrap breakpoint preserves a reusable boundary before
runtime additions.

The Assistant uses [Verbatim Context Retention](../../../../../FlowBlox.AIAssistant/Services/README.md#verbatim-context-retention)
for all retained static content. Its retrieval policies, persistence, and publication
are defined in that guide; providers receive the resulting ordered system messages.
Summary input preserves original prefix content and supplies the shared retention
policy's availability inventory. The summarizer avoids duplicating retained static
descriptions in its output; a marker alone does not establish system availability.

Newly eligible content is appended at its promotion event, without regrouping:
Explanation 1, Type Info 1, Explanation 2, Explanation 3, Type Info 2. The Anthropic
adapter places `ephemeral` on the fixed bootstrap boundary and on the last nonempty
dynamic system block. When the sequence grows, the dynamic marker moves to its new
end while earlier message text and order remain stable. With the separate rolling
conversation and maintenance-boundary markers this uses at most four explicit breakpoints.

Retention copies content without removing or rewriting history. Summary and eligible
system additions are published together after the maintenance branch finishes, so
the previous prefix remains available to summary and summary-compaction requests.
Independent protection markers exclude retained-content results from compaction.

The saved history includes the ordered retained-system list and pending markers;
restore clones both and supplies the exact payloads again. Legacy markers remain
readable. This persistence
does not preserve a remote provider cache across restarts. Cached input still
occupies the full context window; budgeting counts it at full size, while cost and
usage reporting use the provider's cache-read/cache-write counters.

## Adding a provider

- Enable its supported prefix caching by default. Do not introduce provider or
  per-message caching opt-in flags.
- If caching is automatic, preserve the request prefix and let the service manage
  it. If explicit activation or breakpoints are required, implement them inside the
  provider adapter using the native API or SDK.
- Choose useful boundaries within the provider's limits. Maximum cache use does not
  mean marking every message or exceeding the allowed number of breakpoints.
- Preserve system/history order, stable tool definitions, and native tool-call
  metadata. Avoid per-request timestamps, identifiers, or reordered content in the
  stable prefix unless they are required for correctness.
- Keep full input-token accounting separate from cache discounts, and capture the
  provider's cache-read/cache-write usage when available.
- Verify explicit cache behavior against serialized requests. The
  [Anthropic request tests](../../../../../FlowBloxTest/AIAssistant/AnthropicCacheRequestTests.cs)
  cover bootstrap stability, growing explanations, breakpoint limits, and native
  tool-call/result blocks.
