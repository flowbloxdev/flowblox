using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetSkillProjectHandler : ToolHandlerBase
    {
        public override string Name => "GetSkillProject";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns a Skill and its metadata by exact name. Use a name from the session bootstrap for a supplied Skill, " +
            "then execute or debug its returned projectFile directly without making a copy.",
            new JObject
            {
                ["skillProjectName"] = "string (exact project name; case-insensitive)",
                ["mode"] = "string? (distributedData|session; default: distributedData)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var skillProjectName = (args.Value<string>("skillProjectName") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(skillProjectName))
                return Task.FromResult(ToolHandlerUtilities.Fail("skillProjectName is required."));

            var mode = (args.Value<string>("mode") ?? "distributedData").Trim();
            if (!SkillProjectCatalog.IsSupportedMode(mode))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Skill project mode '{mode}' is not supported. Supported modes: session, distributedData."));
            }

            var matches = SkillProjectCatalog.GetProjects(mode)
                .Where(project => string.Equals(
                    project.Value<string>("skillProjectName"),
                    skillProjectName,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matches.Count == 0)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Skill project '{skillProjectName}' was not found in mode '{mode}'."));

            if (matches.Count > 1)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Skill project name '{skillProjectName}' is ambiguous in mode '{mode}'."));

            return Task.FromResult(ToolHandlerUtilities.Ok(matches[0]));
        }
    }
}
