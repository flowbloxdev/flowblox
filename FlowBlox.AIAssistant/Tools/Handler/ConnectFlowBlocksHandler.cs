using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Connect two FlowBlocks.")]
    internal sealed class ConnectFlowBlocksHandler : ConnectDisconnectFlowBlocksHandlerBase
    {
        public override string Name => "ConnectFlowBlocks";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Connects from -> to. In project JSON this edge is represented by adding 'from' into 'to.ReferencedFlowBlocks' (incoming-reference model).",
            new JObject
            {
                ["from"] = "string",
                ["to"] = "string"
            });

        protected override bool Connect => true;
    }
}
