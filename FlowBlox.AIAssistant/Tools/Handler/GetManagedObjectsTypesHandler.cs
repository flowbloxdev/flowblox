using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Discover managed object kinds.")]
    internal sealed class GetManagedObjectsTypesHandler : ToolHandlerBase
    {
        public override string Name => "GetManagedObjectsTypes";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns managed object kinds.");

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var kinds = ToolHandlerUtilities.GetProject()
                .CreateInstances<IManagedObject>()
                .Where(x => x is not BaseFlowBlock)
                .GroupBy(x => x.GetType())
                .Select(group => (FlowBloxReactiveObject)Activator.CreateInstance(group.Key)!)
                .Select(ToolHandlerUtilities.ToTypeInfo)
                .OrderBy(x => x.Value<string>("displayName"));

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["managedObjectKinds"] = new JArray(kinds)
            }));
        }
    }
}
