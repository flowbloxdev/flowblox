using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect a Skill or Project Space project and its execution contract.")]
    internal sealed class GetSkillProjectHandler : ToolHandlerBase
    {
        public override string Name => "GetSkillProject";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns a Skill and its metadata by exact name. Use a name from the session bootstrap for a supplied Skill, " +
            "then execute or debug its returned projectFile directly without making a copy. " +
            "Alternatively inspect Project Space inputs/outputs by projectSpaceGuid; omitted version selects the latest stable version, otherwise current content. Input metadata defaultValue applies when its input parameter is omitted or empty.",
            new JObject
            {
                ["skillProjectName"] = "string? (exact local Skill name; alternative to projectSpaceGuid)",
                ["mode"] = "string? (distributedData|session; default: distributedData)",
                ["projectSpaceGuid"] = "string? (instead of skillProjectName)",
                ["projectSpaceVersion"] = "int? (stable version; default latest stable, or current if none)",
                ["private"] = "bool? (private PS access using current login or remembered credentials; default false)"
            });

        public override async Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(args.Value<string>("projectSpaceGuid")))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(args.Value<string>("skillProjectName")))
                        return ToolHandlerUtilities.Fail("Provide either skillProjectName or projectSpaceGuid, not both.");
                    var project = await ProjectSpaceToolClient.LoadAsync(args, ct).ConfigureAwait(false);
                    var result = SkillProjectCatalog.ToJson(SkillProjectMetadataReader.Read(project, null));
                    result["projectSpaceGuid"] = project.ProjectSpaceGuid;
                    result["projectSpaceVersion"] = project.ProjectSpaceVersion;
                    result["outputs"] = JObject.FromObject(Core.Util.Projects.ProjectMetadataReader.Read(project).Outputs);
                    return ToolHandlerUtilities.Ok(result);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return ToolHandlerUtilities.Fail(exception.Message);
                }
            }
            var skillProjectName = (args.Value<string>("skillProjectName") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(skillProjectName))
                return ToolHandlerUtilities.Fail("skillProjectName or projectSpaceGuid is required.");

            var mode = (args.Value<string>("mode") ?? "distributedData").Trim();
            if (!SkillProjectCatalog.IsSupportedMode(mode))
            {
                return ToolHandlerUtilities.Fail(
                    $"Skill project mode '{mode}' is not supported. Supported modes: session, distributedData.");
            }

            var matches = SkillProjectCatalog.GetProjects(mode)
                .Where(project => string.Equals(
                    project.Value<string>("skillProjectName"),
                    skillProjectName,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matches.Count == 0)
                return ToolHandlerUtilities.Fail($"Skill project '{skillProjectName}' was not found in mode '{mode}'.");

            if (matches.Count > 1)
                return ToolHandlerUtilities.Fail($"Skill project name '{skillProjectName}' is ambiguous in mode '{mode}'.");

            return ToolHandlerUtilities.Ok(matches[0]);
        }
    }
}
