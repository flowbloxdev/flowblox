using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetProjectMetadataHandler : ToolHandlerBase
    {
        public override string Name => "GetProjectMetadata";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns compact metadata for the project currently targeted by project tools.");

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var project = ToolHandlerUtilities.GetProject();
            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["projectName"] = project.ProjectName ?? string.Empty,
                ["projectGuid"] = project.ProjectGuid,
                ["author"] = project.Author ?? string.Empty,
                ["description"] = project.ProjectDescription ?? string.Empty,
                ["notice"] = project.Notice ?? string.Empty,
                ["isSkillProject"] = ToolHandlerUtilities.IsSkillProjectActive()
            }));
        }
    }
}
