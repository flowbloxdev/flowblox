using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Components;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("List root FlowBlock categories.")]
    internal sealed class GetRootCategoriesHandler : ToolHandlerBase
    {
        public override string Name => "GetRootCategories";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns root FlowBlock categories.");

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var categories = FlowBlockCategory.GetAll()
                .Where(x => x.ParentCategory == null)
                .OrderBy(x => x.DisplayName)
                .Select(x => new JObject
                {
                    ["displayName"] = x.DisplayName,
                    ["path"] = new JArray(x.DisplayName)
                });

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["categories"] = new JArray(categories)
            }));
        }
    }
}
