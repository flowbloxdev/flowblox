using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect a FlowBlock snapshot.")]
    internal sealed class GetFlowBlockSnapshotHandler : ToolHandlerBase
    {
        public override string Name => "GetFlowBlockSnapshot";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns flow block snapshot with refs normalized to names.",
            new JObject
            {
                ["name"] = "string"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var name = args.Value<string>("name");
            var flowBlock = ToolHandlerUtilities.GetRegistry()
                .GetFlowBlocks()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (flowBlock == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"FlowBlock '{name}' was not found."));
            }

            var snapshot = ToolHandlerUtilities.CreateSnapshot(
                "flowBlock",
                flowBlock.Name,
                flowBlock.GetType().FullName,
                flowBlock,
                flowBlock.ReferencedFlowBlocks.Select(x => x.Name));

            return Task.FromResult(ToolHandlerUtilities.Ok(snapshot));
        }
    }
}
