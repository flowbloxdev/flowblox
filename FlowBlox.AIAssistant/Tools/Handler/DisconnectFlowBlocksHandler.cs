using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Disconnect two FlowBlocks.")]
    internal sealed class DisconnectFlowBlocksHandler : ConnectDisconnectFlowBlocksHandlerBase
    {
        public override string Name => "DisconnectFlowBlocks";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Disconnects from -> to.",
            new JObject
            {
                ["from"] = "string",
                ["to"] = "string"
            });

        protected override bool Connect => false;
    }
}
