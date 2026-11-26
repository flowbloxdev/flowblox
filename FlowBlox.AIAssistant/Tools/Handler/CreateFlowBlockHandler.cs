using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Create a FlowBlock.", IncludeContractInBootstrap = true)]
    internal sealed class CreateFlowBlockHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "CreateFlowBlock";
        public override bool IsLayoutRelevantForAutoAdjustment => true;

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Creates flow block and always assigns Location (x,y).",
            new JObject
            {
                ["typeFullName"] = "string",
                ["name"] = "string?",
                ["x"] = "int?",
                ["y"] = "int?"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var type = ToolHandlerUtilities.ResolveType(args.Value<string>("typeFullName"));
            if (type == null || !typeof(BaseFlowBlock).IsAssignableFrom(type) || type.IsAbstract)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail("typeFullName invalid."));
            }

            var registry = ToolHandlerUtilities.GetRegistry();
            var flowBlock = registry.CreateFlowBlockUnregistered(type);

            var requestedName = args.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                flowBlock.Name = requestedName;
            }

            var duplicateNameExists = registry.GetFlowBlocks()
                .Any(x => string.Equals(x.Name, flowBlock.Name, StringComparison.OrdinalIgnoreCase));
            if (duplicateNameExists)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"FlowBlock name '{flowBlock.Name}' already exists."));
            }

            var x = args.Value<int?>("x");
            var y = args.Value<int?>("y");
            var targetLocation = DetermineLocation(registry, x, y);
            flowBlock.Location = targetLocation;

            registry.PostProcessFlowBlockCreated(flowBlock);
            registry.RegisterFlowBlock(flowBlock);

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["created"] = true,
                ["name"] = flowBlock.Name,
                ["typeFullName"] = flowBlock.GetType().FullName,
                ["location"] = new JObject
                {
                    ["x"] = flowBlock.Location.X,
                    ["y"] = flowBlock.Location.Y
                }
            }));
        }

        private static System.Drawing.Point DetermineLocation(
            FlowBlox.Core.Provider.Registry.FlowBloxRegistry registry,
            int? requestedX,
            int? requestedY)
        {
            const int defaultX = 50;
            const int centerLineY = 400;
            const int horizontalStep = 380; // 328px width + spacing

            if (requestedX.HasValue && requestedY.HasValue)
            {
                return new System.Drawing.Point(requestedX.Value, requestedY.Value);
            }

            var flowBlocks = registry.GetFlowBlocks().ToList();
            if (flowBlocks.Count == 0)
            {
                return new System.Drawing.Point(requestedX ?? defaultX, requestedY ?? centerLineY);
            }

            var maxX = flowBlocks.Max(x => x.Location.X);
            var autoX = maxX + horizontalStep;

            return new System.Drawing.Point(requestedX ?? autoX, requestedY ?? centerLineY);
        }
    }
}