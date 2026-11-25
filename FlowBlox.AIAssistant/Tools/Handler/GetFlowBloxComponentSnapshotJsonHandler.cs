using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect compact component JSON.")]
    internal sealed class GetFlowBloxComponentSnapshotJsonHandler : ToolHandlerBase
    {
        public override string Name => "GetFlowBloxComponentSnapshotJSON";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Generic compact snapshot. kind=flowBlock|managedObject.",
            new JObject
            {
                ["kind"] = "string",
                ["name"] = "string"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var kind = args.Value<string>("kind");
            var name = args.Value<string>("name");

            if (string.Equals(kind, "flowBlock", StringComparison.OrdinalIgnoreCase))
            {
                var flowBlock = ToolHandlerUtilities.GetRegistry()
                    .GetFlowBlocks()
                    .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

                if (flowBlock == null)
                {
                    return Task.FromResult(ToolHandlerUtilities.Fail($"FlowBlock '{name}' was not found."));
                }

                var flowBlockSnapshot = ToolHandlerUtilities.CreateSnapshot(
                    "flowBlock",
                    flowBlock.Name,
                    flowBlock.GetType().FullName,
                    flowBlock,
                    flowBlock.ReferencedFlowBlocks.Select(x => x.Name));

                return Task.FromResult(ToolHandlerUtilities.Ok(flowBlockSnapshot));
            }

            if (string.Equals(kind, "managedObject", StringComparison.OrdinalIgnoreCase))
            {
                var managedObject = ToolHandlerUtilities.GetRegistry()
                    .GetManagedObjects()
                    .OfType<FlowBloxComponent>()
                    .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

                if (managedObject == null)
                {
                    return Task.FromResult(ToolHandlerUtilities.Fail($"Managed object '{name}' was not found."));
                }

                var managedObjectSnapshot = ToolHandlerUtilities.CreateSnapshot(
                    "managedObject",
                    managedObject.Name,
                    managedObject.GetType().FullName,
                    managedObject,
                    null);

                return Task.FromResult(ToolHandlerUtilities.Ok(managedObjectSnapshot));
            }

            return Task.FromResult(ToolHandlerUtilities.Fail("kind must be 'flowBlock' or 'managedObject'."));
        }
    }
}