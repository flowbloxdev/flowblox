using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetSkillProjectsHandler : ToolHandlerBase
    {
        public override string Name => "GetSkillProjects";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Lists Skills and their metadata from the current session or distributed FlowBlox data, optionally filtered by name or description. " +
            "Both modes return projectFile for direct ExecuteExternalProject or RunProjectDebugTest calls; distributed projects do not need to be copied first. " +
            "Use GetSkillProject instead when the exact name is already known from the session bootstrap.",
            new JObject
            {
                ["mode"] = "string? (session|distributedData; default: session)",
                ["filter"] = "string? (comma/space-separated terms; case-insensitive contains OR match against project name, short description, or description)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var mode = (args.Value<string>("mode") ?? "session").Trim();
            var filter = (args.Value<string>("filter") ?? string.Empty).Trim();
            var filterTerms = ToolHandlerUtilities.ParseContainsSearchTerms(args["filter"]);
            if (!SkillProjectCatalog.IsSupportedMode(mode))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Skill project mode '{mode}' is not supported. Supported modes: session, distributedData."));
            }

            var projects = SkillProjectCatalog.GetProjects(mode).ToList();
            projects = FilterProjects(projects, filterTerms).ToList();
            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["mode"] = mode,
                ["filter"] = filter,
                ["filterTerms"] = new JArray(filterTerms),
                ["matchMode"] = "CaseInsensitiveContainsOR",
                ["skillProjects"] = new JArray(projects.OrderBy(
                    x => x.Value<string>("skillProjectName"),
                    StringComparer.OrdinalIgnoreCase))
            }));
        }

        private static IEnumerable<JObject> FilterProjects(
            IEnumerable<JObject> projects,
            IReadOnlyCollection<string> filterTerms)
        {
            return projects.Where(x => ToolHandlerUtilities.ContainsAnySearchTerm(
                [x.Value<string>("skillProjectName"), x.Value<string>("shortDescription"), x.Value<string>("description")],
                filterTerms));
        }
    }
}
