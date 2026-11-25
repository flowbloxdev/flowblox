using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Find configurable FlowBlox options.")]
    internal sealed class SearchOptionsHandler : ToolHandlerBase
    {
        public override string Name => "SearchOptions";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Searches FlowBlox options by name/display name/description using case-insensitive contains matching (OR). Empty searchForNames lists all options.",
            new JObject
            {
                ["searchForNames"] = "string? (comma/space-separated terms; OR search, case-insensitive contains)",
                ["usageHint"] = "Requires AI Assistant Configuration > Permissions > Options access: Read only or Read and write. Password values are masked."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                ToolHandlerUtilities.EnsureOptionsReadAccess();
                return Task.FromResult(ToolHandlerUtilities.SearchOptions(args));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }
    }
}
