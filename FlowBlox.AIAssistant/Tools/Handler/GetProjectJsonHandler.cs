using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect active project JSON.", IncludeContractInBootstrap = true)]
    internal sealed class GetProjectJsonHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "GetProjectJson";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns active project JSON export.");

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var project = ToolHandlerUtilities.GetProject();

            // Collect current state before export and order by dependency for clean top-level JSON references.
            project.RefreshOrderedTopLevelCollectionsForSerialization();

            var payload = new JObject
            {
                ["projectJson"] = JsonConvert.SerializeObject(project, JsonSettings.ProjectExportForAiAssistant()),
                ["flowBlockCount"] = project.FlowBloxRegistry.GetFlowBlocks().Count(),
                ["managedObjectCount"] = project.FlowBloxRegistry.GetManagedObjects().Count()
            };

            return Task.FromResult(ToolHandlerUtilities.Ok(payload));
        }
    }
}