using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Util.FlowBlocks;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Resolve an associated FlowBlock property.", IncludeContractInBootstrap = true)]
    internal sealed class ResolveAssociatedFlowBlockHandler : ToolHandlerBase
    {
        public override string Name => "ResolveAssociatedFlowBlock";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Resolves an AssociatedFlowBlockResolvable property of a flow block. Resolution order: explicit property value first, otherwise previous flow block on path by required type.",
            new JObject
            {
                ["flowBlockName"] = "string",
                ["property"] = "string (property name on the flow block)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var registry = ToolHandlerUtilities.GetRegistry();
            var flowBlockName = args.Value<string>("flowBlockName");
            var propertyName = args.Value<string>("property");

            if (string.IsNullOrWhiteSpace(flowBlockName))
                return Task.FromResult(ToolHandlerUtilities.Fail("flowBlockName is required."));

            if (string.IsNullOrWhiteSpace(propertyName))
                return Task.FromResult(ToolHandlerUtilities.Fail("property is required."));

            var flowBlock = ToolHandlerUtilities.ResolveFlowBlockByName(registry, flowBlockName);
            if (flowBlock == null)
                return Task.FromResult(ToolHandlerUtilities.Fail($"FlowBlock '{flowBlockName}' was not found."));

            AssociatedFlowBlockResolutionResult resolution;
            try
            {
                resolution = AssociatedFlowBlockResolver.Resolve(flowBlock, propertyName);
            }
            catch (InvalidOperationException ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }

            var resolutionSource = resolution.Source switch
            {
                AssociatedFlowBlockResolutionSource.Property => "property",
                AssociatedFlowBlockResolutionSource.Path => "path",
                _ => "none"
            };

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["flowBlockName"] = flowBlock.Name,
                ["property"] = propertyName,
                ["associatedFlowBlockName"] = resolution.FlowBlock?.Name ?? string.Empty,
                ["resolutionSource"] = resolutionSource,
                ["resolved"] = resolution.Resolved
            }));
        }
    }
}
