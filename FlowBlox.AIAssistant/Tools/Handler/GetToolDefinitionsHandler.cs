using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler;

[ToolPurpose("Load full tool descriptions and argument contracts.", IncludeContractInBootstrap = true)]
internal sealed class GetToolDefinitionsHandler(Func<IReadOnlyList<ToolDefinition>> getDefinitions) : ToolHandlerBase
{
    public override string Name => "GetToolDefinitions";

    public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
        Name,
        "Returns the complete descriptions and argument contracts for named tools available in the current scope. " +
        "Load the contract before using a tool; do not guess its arguments. " +
        "Already available contracts are referenced in current history or system messages.",
        new JObject
        {
            ["toolNames"] = "string[] (required, non-empty) - tool names from the available tools catalog"
        });

    public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var requested = args["toolNames"] as JArray;
        if (requested == null || requested.Count == 0 ||
            requested.Any(token => token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>())))
        {
            return Task.FromResult(ToolHandlerUtilities.Fail("toolNames must be a non-empty array of tool names."));
        }

        var available = getDefinitions();
        var resolver = new ToolNameResolver(available.Select(definition => definition.Name));
        var selected = new List<ToolDefinition>();
        foreach (var token in requested)
        {
            var name = resolver.Resolve(token.Value<string>()!);
            if (name == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Tool '{token.Value<string>()}' is unavailable in the current scope.",
                    new JObject { ["availableToolNames"] = new JArray(available.Select(definition => definition.Name)) }));
            }
            if (!selected.Any(definition => definition.Name == name))
                selected.Add(available.Single(definition => definition.Name == name));
        }

        var definitions = new JArray();
        var response = ToolHandlerUtilities.Ok(new JObject { ["definitions"] = definitions });
        foreach (var definition in selected)
        {
            var key = GetRetentionKey(definition.Name);
            var location = ToolHandlerUtilities.GetRetainedContentLocation(key);
            var contract = new JObject
            {
                ["name"] = definition.Name,
                ["description"] = definition.Description,
                ["argumentsSchema"] = definition.ArgumentsSchema.DeepClone()
            };
            var index = definitions.Count;
            definitions.Add(location == null ? contract : new JObject
            {
                ["name"] = definition.Name,
                ["retainedContentKey"] = key,
                ["message"] = location == "system"
                    ? "The full tool contract is already available as a system message."
                    : "The full tool contract is already available in the current history."
            });
            ToolHandlerUtilities.RegisterRetainedContent(response, new AssistantRetainedContent
            {
                Key = key,
                Title = definition.Name,
                Kind = AssistantSystemMessageKind.ToolDefinition,
                Content = location == null ? "Tool contract:\n" + contract.ToString(Formatting.None) : string.Empty,
                PayloadPath = $"definitions[{index}]"
            });
        }
        return Task.FromResult(response);
    }

    internal static string GetRetentionKey(string toolName) => "tool-definition:" + toolName;
}
