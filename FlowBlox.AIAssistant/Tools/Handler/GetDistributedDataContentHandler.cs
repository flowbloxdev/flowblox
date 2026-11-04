using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetDistributedDataContentHandler : ToolHandlerBase
    {
        public override string Name => "GetDistributedDataContent";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns one file from FlowBlox's central distributed data directory.",
            new JObject
            {
                ["dataType"] = "string (python, skill_project, demo_prompts, or example_project)",
                ["fileName"] = "string (plain filename including extension)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var dataType = (args.Value<string>("dataType") ?? string.Empty).Trim();
                var fileName = (args.Value<string>("fileName") ?? string.Empty).Trim();
                return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
                {
                    ["dataType"] = dataType,
                    ["fileName"] = fileName,
                    ["content"] = DistributedDataCatalog.ReadContent(dataType, fileName)
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }
    }
}
