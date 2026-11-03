using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetAuxiliaryProjectHandler : ToolHandlerBase
    {
        public override string Name => "GetAuxiliaryProject";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns one auxiliary project's metadata by exact project name, including projectFile, inputFields, " +
            "inputFieldListOfValues when present, and outputFields. Use a name from the session bootstrap for a supplied project, " +
            "then execute or debug its returned projectFile directly without making a copy.",
            new JObject
            {
                ["projectName"] = "string (exact project name; case-insensitive)",
                ["mode"] = "string? (distributedData|session; default: distributedData)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var projectName = (args.Value<string>("projectName") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(projectName))
                return Task.FromResult(ToolHandlerUtilities.Fail("projectName is required."));

            var mode = (args.Value<string>("mode") ?? "distributedData").Trim();
            if (!AuxiliaryProjectCatalog.IsSupportedMode(mode))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Auxiliary project mode '{mode}' is not supported. Supported modes: session, distributedData."));
            }

            var matches = AuxiliaryProjectCatalog.GetProjects(mode)
                .Where(project => string.Equals(
                    project.Value<string>("projectName"),
                    projectName,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matches.Count == 0)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Auxiliary project '{projectName}' was not found in mode '{mode}'."));

            if (matches.Count > 1)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Auxiliary project name '{projectName}' is ambiguous in mode '{mode}'."));

            return Task.FromResult(ToolHandlerUtilities.Ok(matches[0]));
        }
    }
}
