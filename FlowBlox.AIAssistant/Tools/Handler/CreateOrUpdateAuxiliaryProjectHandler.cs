using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class CreateOrUpdateAuxiliaryProjectHandler : ToolHandlerBase
    {
        private const string MetadataOnlyMode = "metadata_only";
        private const string FullContentMode = "full_content";

        public override string Name => "CreateOrUpdateAuxiliaryProject";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Creates or updates a closed auxiliary-project file in the current session. Use AuxiliaryProjectEditing for live edits.",
            new JObject
            {
                ["projectName"] = "string (required; also determines the filename)",
                ["existingProjectName"] = "string? (metadata_only rename: current name; projectName is the new name)",
                ["description"] = "string? (required for metadata_only; optional metadata override for full_content)",
                ["shortDescription"] = "string? (compact capability summary; optional metadata override)",
                ["mode"] = "string (metadata_only|full_content)",
                ["projectJson"] = "string? (complete full_content source including description unless overridden; use exactly one content source)",
                ["copyFromDistributedData"] = "object? ({ dataType: 'auxiliary_project', fileName: string }); alternative full_content source; use exactly one"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var sessionId = ToolHandlerUtilities.CurrentSessionGuid;
                if (AuxiliaryProjectSessionStore.IsActive(sessionId))
                {
                    return Task.FromResult(ToolHandlerUtilities.Fail(
                        "File-level auxiliary-project changes are not allowed while an auxiliary project is open. Save or close it first."));
                }

                var projectName = (args.Value<string>("projectName") ?? string.Empty).Trim();
                var existingProjectName = (args.Value<string>("existingProjectName") ?? string.Empty).Trim();
                var description = (args.Value<string>("description") ?? string.Empty).Trim();
                var shortDescription = (args.Value<string>("shortDescription") ?? string.Empty).Trim();
                var mode = (args.Value<string>("mode") ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(projectName))
                    return Task.FromResult(ToolHandlerUtilities.Fail("projectName is required."));
                if (mode is not (MetadataOnlyMode or FullContentMode))
                    return Task.FromResult(ToolHandlerUtilities.Fail("mode must be metadata_only or full_content."));
                if (mode == FullContentMode && !string.IsNullOrWhiteSpace(existingProjectName))
                    return Task.FromResult(ToolHandlerUtilities.Fail("existingProjectName is only supported for metadata_only."));

                var project = mode == MetadataOnlyMode
                    ? ResolveMetadataOnlyProject(
                        sessionId,
                        string.IsNullOrWhiteSpace(existingProjectName) ? projectName : existingProjectName,
                        description,
                        shortDescription,
                        args)
                    : ResolveFullContentProject(description, shortDescription, args);
                var result = AuxiliaryProjectSessionStore.CreateOrUpdateFile(
                    sessionId,
                    projectName,
                    project,
                    existingProjectName);

                return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
                {
                    ["operation"] = result.Created ? "created" : "updated",
                    ["mode"] = mode,
                    ["projectName"] = result.Project.ProjectName,
                    ["shortDescription"] = result.Project.AssistantMetadata.ShortDescription,
                    ["description"] = result.Project.ProjectDescription,
                    ["projectGuid"] = result.Project.ProjectGuid,
                    ["projectFile"] = result.ProjectFile
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }

        private static FlowBloxProject ResolveMetadataOnlyProject(
            string sessionId,
            string projectName,
            string description,
            string shortDescription,
            JObject args)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException("description is required for metadata_only.");
            if (args["projectJson"] != null || args["copyFromDistributedData"] != null)
                throw new InvalidOperationException("metadata_only does not accept a content source.");

            if (!AuxiliaryProjectSessionStore.ProjectExists(sessionId, projectName))
                return new FlowBloxProject
                {
                    ProjectDescription = description,
                    AssistantMetadata = new AssistantMetadata { ShortDescription = shortDescription }
                };

            var project = AuxiliaryProjectSerializer.FromFile(
                AuxiliaryProjectSessionStore.GetStoredProjectFile(sessionId, projectName));
            project.ProjectDescription = description;
            if (!string.IsNullOrWhiteSpace(shortDescription))
                project.AssistantMetadata.ShortDescription = shortDescription;
            return project;
        }

        private static FlowBloxProject ResolveFullContentProject(
            string description,
            string shortDescription,
            JObject args)
        {
            var projectJson = args.Value<string>("projectJson");
            var distributedData = args["copyFromDistributedData"] as JObject;
            if (string.IsNullOrWhiteSpace(projectJson) == (distributedData == null))
            {
                throw new InvalidOperationException(
                    "full_content requires exactly one source: projectJson or copyFromDistributedData.");
            }

            if (distributedData != null)
            {
                projectJson = DistributedDataCopyResolver.Resolve(
                    distributedData,
                    DistributedDataCatalog.AuxiliaryProjectType).Content;
            }

            var project = AuxiliaryProjectSerializer.FromJson(projectJson!);
            if (!string.IsNullOrWhiteSpace(description))
                project.ProjectDescription = description;
            if (!string.IsNullOrWhiteSpace(shortDescription))
                project.AssistantMetadata.ShortDescription = shortDescription;
            return project;
        }
    }
}
