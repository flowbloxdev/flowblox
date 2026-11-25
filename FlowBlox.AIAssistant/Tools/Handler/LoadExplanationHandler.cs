using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler;

[ToolPurpose("Load domain guidance.", IncludeContractInBootstrap = true)]
internal sealed class LoadExplanationHandler : ToolHandlerBase
{
    public override string Name => "LoadExplanation";
    public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
        Name,
        "Loads on-demand guidance in tool history; Verbatim Context Retention copies it into system messages at the next context transition.",
        new JObject
        {
            ["key"] = "string"
        });

    public override Task<ToolResponse> HandleAsync(
        JObject args,
        CancellationToken ct)
    {
        var key = args.Value<string>("key");
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(ToolHandlerUtilities.Fail("key is required."));

        if (!AssistantPromptCatalog.TryGetEntry(key, out var entry) || entry == null)
            return Task.FromResult(ToolHandlerUtilities.Fail(
                $"Explanation entry '{key}' was not found.",
                new JObject
                {
                    ["availableKeys"] = new JArray(AssistantPromptCatalog.GetAllEntries()
                        .Select(x => x.Key))
                }));

        if (entry.IsIncludedInInitialPrompt)
            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["key"] = entry.Key,
                ["loaded"] = false,
                ["message"] = $"Already loaded: '{entry.Title}' is part of the initial system guidance."
            }));

        var retentionKey = "explanation:" + entry.Key;
        var location = ToolHandlerUtilities.GetRetainedContentLocation(retentionKey);
        var alreadyLoaded = location != null;
        var response = ToolHandlerUtilities.Ok(new JObject
        {
            ["key"] = entry.Key,
            ["loaded"] = !alreadyLoaded,
            ["message"] = alreadyLoaded
                ? $"'{entry.Title}' is already available in the current {location}."
                : $"'{entry.Title}' is available below and will be retained at the next context transition."
        });
        if (!alreadyLoaded)
            response.Result["content"] = entry.Content;
        ToolHandlerUtilities.RegisterRetainedContent(response, new AssistantRetainedContent
        {
            Key = retentionKey,
            Title = entry.Title,
            Kind = AssistantSystemMessageKind.Explanation,
            Content = alreadyLoaded ? string.Empty : entry.Content
        });
        return Task.FromResult(response);
    }
}
