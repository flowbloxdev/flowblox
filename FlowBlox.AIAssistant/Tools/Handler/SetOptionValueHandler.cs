using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Set a non-password FlowBlox option.")]
    internal sealed class SetOptionValueHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "SetOptionValue";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Sets one FlowBlox option by key and stringValue. Boolean options require 'true' or 'false'. Password options cannot be written.",
            new JObject
            {
                ["key"] = "string (option name/key)",
                ["stringValue"] = "string (Text as-is, Integer whole number, Boolean 'true' or 'false')",
                ["usageHint"] = "Requires AI Assistant Configuration > Permissions > Options access: Read and write."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                ToolHandlerUtilities.EnsureOptionsWriteAccess();
                return Task.FromResult(ToolHandlerUtilities.SetOptionValue(args));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }
    }
}
