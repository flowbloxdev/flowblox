using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Discover concrete implementations of an interface or base type.")]
    internal sealed class GetSupportedTypesHandler : ToolHandlerBase
    {
        public override string Name => "GetSupportedTypes";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns non-abstract supported implementation types for an interface/base type, including fullName, displayName and description metadata. Primarily use this for FlowBloxReactiveObject hierarchies (FlowBlocks, ManagedObjects, nested ReactiveObjects).",
            new JObject
            {
                ["baseTypeFullName"] = "string"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var baseTypeFullName = args.Value<string>("baseTypeFullName");
            var baseType = ToolHandlerUtilities.ResolveType(baseTypeFullName);
            if (baseType == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"Type '{baseTypeFullName}' could not be resolved."));
            }

            var supportedTypes = ToolHandlerUtilities.GetSupportedTypeInfos(baseType);
            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["baseTypeFullName"] = baseType.FullName ?? baseType.Name,
                ["supportedTypes"] = new JArray(supportedTypes)
            }));
        }
    }
}