using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler;

internal sealed class GetPSProjectsHandler : ToolHandlerBase
{
    public override string Name => "GetPSProjects";

    public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
        Name,
        "Searches Project Space names and descriptions without downloading projects. Returns descriptions, GUIDs and stable versions. Private/all requires current login or remembered credentials. Inspect a match with GetSkillProject(projectSpaceGuid=...).",
        new JObject
        {
            ["searchForNames"] = "string? (comma/space-separated terms; case-insensitive contains OR; empty lists all)",
            ["classification"] = "string? (Solution|Function; empty or omitted lists both)",
            ["visibility"] = "string? (public|private|all; default public; private means your private projects)"
        });

    public override async Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var visibility = (args.Value<string>("visibility") ?? "public").Trim().ToLowerInvariant();
            if (visibility is not ("public" or "private" or "all"))
                return ToolHandlerUtilities.Fail("visibility must be public, private or all.");
            var classificationText = args.Value<string>("classification");
            FbProjectClassification? classification = null;
            if (!string.IsNullOrWhiteSpace(classificationText))
            {
                if (!Enum.TryParse<FbProjectClassification>(classificationText, true, out var parsed) || !Enum.IsDefined(parsed))
                    return ToolHandlerUtilities.Fail("classification must be Solution or Function.");
                classification = parsed;
            }
            var client = await ProjectSpaceToolClient.CreateAsync(visibility != "public").ConfigureAwait(false);
            var projects = new List<FbProjectResult>();
            if (visibility != "private")
            {
                var response = await client.Api.GetProjectsAsync(classification: classification).ConfigureAwait(false);
                ProjectSpaceToolClient.RequireSuccess(response);
                projects.AddRange((response.ResultObject ?? []).Where(project => project.Visibility == FbProjectVisibility.Public));
            }
            if (visibility != "public")
            {
                var response = await client.Api.GetProjectsAsync(client.Token, mine: true, classification: classification).ConfigureAwait(false);
                ProjectSpaceToolClient.RequireSuccess(response);
                projects.AddRange((response.ResultObject ?? []).Where(project => project.Visibility == FbProjectVisibility.Private));
            }
            ct.ThrowIfCancellationRequested();
            var terms = ToolHandlerUtilities.ParseContainsSearchTerms(args["searchForNames"]);
            var matches = projects
                .Where(project => ToolHandlerUtilities.ContainsAnySearchTerm([project.Name, project.Description], terms))
                .GroupBy(project => project.Guid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .Select(project => new JObject
                {
                    ["projectSpaceGuid"] = project.Guid,
                    ["projectName"] = project.Name,
                    ["description"] = project.Description,
                    ["visibility"] = project.Visibility.ToString(),
                    ["classification"] = project.Classification.ToString(),
                    ["usedByCount"] = project.UsedByCount,
                    ["stableVersions"] = new JArray(ProjectSpaceToolClient.StableVersions(project)),
                    ["projectSpaceVersion"] = ProjectSpaceToolClient.StableVersions(project).Cast<int?>().FirstOrDefault()
                });
            return ToolHandlerUtilities.Ok(new JObject
            {
                ["visibility"] = visibility,
                ["searchForNames"] = new JArray(terms),
                ["matchMode"] = "CaseInsensitiveContainsOR",
                ["projects"] = new JArray(matches)
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ToolHandlerUtilities.Fail(exception.Message);
        }
    }
}
