using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler;

[ToolPurpose("Inspect type properties, enums and nested type metadata.")]
internal sealed class GetTypeKindsInfoHandler : ToolHandlerBase
{
    public override string Name => "GetTypeKindsInfo";
    public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns compact metadata for FlowBloxReactiveObject types and Enums. " +
            "Property flags: N=nullable,W=writable,S=simple,E=enum,C=collection. " +
            "UI flags: C=create,L=link,U=unlink,E=edit,D=delete,[FLD]=field,[FL]=file,[DI]=directory. " +
            "Attributes: Name(arg;member=value). " + ToolHandlerUtilities.TypeInfoCacheDescription,
            new JObject
            {
                ["typeFullName"] = "string"
            });

    public override Task<ToolResponse> HandleAsync(
        JObject args,
        CancellationToken ct) =>
        Task.FromResult(ToolHandlerUtilities.CreateUnifiedTypeInfoResponse(args.Value<string>("typeFullName")));
}
