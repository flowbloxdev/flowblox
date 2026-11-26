using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Update FlowBlock properties by JSON path.", IncludeContractInBootstrap = true)]
    internal sealed class UpdateFlowBlockHandler : ToolHandlerBase
    {
        public override string Name => "UpdateFlowBlock";
        public override bool IsLayoutRelevantForAutoAdjustment => true;

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Updates simple/reactive properties on a flow block via JSON-path syntax /Property/0/Nested. Do not update managed objects indirectly.",
            new JObject
            {
                ["name"] = "string",
                ["path"] = "string?",
                ["value"] = "any?",
                ["updates"] = "[{path,value}]?",
                ["contractHint"] = "Path-based updates with Link/Unlink/Delete terminals. Typed index '/Collection/<index>:<FullTypeName>/...' is optional and mainly needed for abstract/base or polymorphic FlowBloxReactiveObject-only collections (see kind/supportedTypes). Create ManagedObjects/FlowBlocks via CreateManagedObject/CreateFlowBlock, then assign via Update.",
                ["explanationHint"] = "For full update/delete semantics and automatic dependency handling, see the explanation 'explaining_edit_and_delete'. Hard rule for test/generator work: before writing TestDefinitions and/or GenerationStrategies, first call LoadExplanation('explaining_test_definitions') and LoadExplanation('explaining_generators').",
                ["resolverExamples"] = new JArray
                {
                    new JObject { ["resolveFlowBlockByName"] = "FlowBlockName" },
                    new JObject { ["resolveManagedObjectByName"] = "ManagedObjectName" },
                    new JObject { ["resolveFieldElementByFQName"] = "$FlowBlock::FieldName" }
                }
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

            var response = ToolHandlerUtilities.ApplyUpdates(args, flowBlock, registry, "flowBlock", flowBlock.Name);
            return Task.FromResult(response);
        }
    }
}
