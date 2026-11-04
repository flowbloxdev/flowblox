using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetDistributedDataHandler : ToolHandlerBase
    {
        public override string Name => "GetDistributedData";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Lists filenames distributed with FlowBlox in the central application data directory.",
            new JObject
            {
                ["dataType"] = "string? (optional: python, skill_project, demo_prompts, or example_project)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var requestedType = (args.Value<string>("dataType") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(requestedType) && !DistributedDataCatalog.IsSupportedType(requestedType))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Distributed data type '{requestedType}' is not supported."));
            }

            var types = DistributedDataCatalog.GetDataTypes()
                .Where(x => string.IsNullOrWhiteSpace(requestedType) ||
                            string.Equals(x.Name, requestedType, StringComparison.OrdinalIgnoreCase))
                .Select(x => new JObject
                {
                    ["dataType"] = x.Name,
                    ["fileNames"] = new JArray(x.FileNames)
                });

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["dataTypes"] = new JArray(types)
            }));
        }
    }
}
