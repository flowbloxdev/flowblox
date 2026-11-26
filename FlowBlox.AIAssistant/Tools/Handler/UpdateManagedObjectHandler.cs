using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Update managed object properties by JSON path.", IncludeContractInBootstrap = true)]
    internal sealed class UpdateManagedObjectHandler : ToolHandlerBase
    {
        public override string Name => "UpdateManagedObject";
        public override bool IsLayoutRelevantForAutoAdjustment => true;

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Updates simple/reactive properties on a managed object via JSON-path syntax /Property/0/Nested.",
            new JObject
            {
                ["name"] = "string",
                ["path"] = "string?",
                ["value"] = "any?",
                ["updates"] = "[{path,value}]?",
                ["contractHint"] = "Path-based updates with Link/Unlink/Delete terminals. Typed index '/Collection/<index>:<FullTypeName>/...' is optional and mainly needed for abstract/base or polymorphic FlowBloxReactiveObject-only collections (see kind/supportedTypes). Create ManagedObjects/FlowBlocks via CreateManagedObject/CreateFlowBlock, then assign via Update.",
                ["explanationHint"] = "For full update/delete semantics and automatic dependency handling, see the explanation 'explaining_edit_and_delete'. Hard rule for test/generator work: before writing FlowBloxTestDefinition and/or generation-strategy-related structures, first call LoadExplanation('explaining_test_definitions') and LoadExplanation('explaining_generators').",
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

            var managedObject = !string.IsNullOrWhiteSpace(name) && FieldElement.IsFullyQualifiedName(name)
                ? ToolHandlerUtilities.ResolveFieldElementByFQName(registry, name)
                : ToolHandlerUtilities.ResolveManagedObjectByName(registry, name) as FlowBloxComponent;

            if (managedObject == null)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Managed object '{name}' was not found."));

            var response = ToolHandlerUtilities.ApplyUpdates(args, managedObject, registry, "managedObject", managedObject.Name);
            return Task.FromResult(response);
        }
    }
}
