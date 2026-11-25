using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Delete a FlowBlock and handle references.")]
    internal sealed class DeleteFlowBlockHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "DeleteFlowBlock";
        public override bool IsLayoutRelevantForAutoAdjustment => true;

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Deletes a flow block and automatically handles dependent references.",
            new JObject
            {
                ["name"] = "string",
                ["usageHint"] = "result.deletionReport is present only when dependencies were handled because of this deletion; absence means there were none. reviewStatus describes whether an entry is informational, should be reviewed, or requires manual follow-up. Entries with automaticallyRemoved=false require manual correction."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var registry = ToolHandlerUtilities.GetRegistry();
            var name = args.Value<string>("name");

            var flowBlock = registry.GetFlowBlocks()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (flowBlock == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"FlowBlock '{name}' was not found."));
            }

            var report = FlowBloxDeletionHelper.Analyze(registry, flowBlock);
            var action = FlowBloxDeletionHelper.CreateAction(registry, report);
            action.Invoke();
            ToolHandlerUtilities.RegisterDeletionHistory(action);

            var result = new JObject
            {
                ["deleted"] = true,
                ["name"] = name
            };
            ToolHandlerUtilities.AddDeletionReportIfAny(result, report);
            return Task.FromResult(ToolHandlerUtilities.Ok(result));
        }
    }
}
