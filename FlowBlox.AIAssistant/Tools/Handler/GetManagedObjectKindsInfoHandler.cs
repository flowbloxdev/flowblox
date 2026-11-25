using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Interfaces;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler;

[ToolPurpose("Inspect managed object type metadata.")]
internal sealed class GetManagedObjectKindsInfoHandler : ToolHandlerBase
{
    public override string Name => "GetManagedObjectKindsInfo";
    public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns compact managed object metadata. " + ToolHandlerUtilities.TypeInfoCacheDescription,
            new JObject
            {
                ["typeFullName"] = "string (assembly-qualified names supported)"
            });

    public override Task<ToolResponse> HandleAsync(
        JObject args,
        CancellationToken ct)
    {
        var name = args.Value<string>("typeFullName");
        var type = ToolHandlerUtilities.ResolveType(name);
        return Task.FromResult(type == null || !typeof(IManagedObject).IsAssignableFrom(type)
            ? ToolHandlerUtilities.Fail($"Type '{name}' is not a managed object type.")
            : ToolHandlerUtilities.CreateUnifiedTypeInfoResponse(name));
    }
}
