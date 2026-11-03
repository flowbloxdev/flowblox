using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetAuxiliaryProjectsHandler : ToolHandlerBase
    {
        public override string Name => "GetAuxiliaryProjects";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Lists auxiliary projects from the current session or distributed FlowBlox data, optionally filtered by project name or description. " +
            "Both modes return projectFile for direct ExecuteExternalProject or RunProjectDebugTest calls; distributed projects do not need to be copied first. " +
            "Use GetAuxiliaryProject instead when the exact name is already known from the session bootstrap.",
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
            if (!AuxiliaryProjectCatalog.IsSupportedMode(mode))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Auxiliary project mode '{mode}' is not supported. Supported modes: session, distributedData."));
            }

            var projects = AuxiliaryProjectCatalog.GetProjects(mode).ToList();
            projects = FilterProjects(projects, filterTerms).ToList();
            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["mode"] = mode,
                ["filter"] = filter,
                ["filterTerms"] = new JArray(filterTerms),
                ["matchMode"] = "CaseInsensitiveContainsOR",
                ["projects"] = new JArray(projects.OrderBy(
                    x => x.Value<string>("projectName"),
                    StringComparer.OrdinalIgnoreCase))
            }));
        }

        private static IEnumerable<JObject> FilterProjects(
            IEnumerable<JObject> projects,
            IReadOnlyCollection<string> filterTerms)
        {
            return projects.Where(x => ToolHandlerUtilities.ContainsAnySearchTerm(
                [x.Value<string>("projectName"), x.Value<string>("shortDescription"), x.Value<string>("description")],
                filterTerms));
        }
    }
}
