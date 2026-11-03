using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class AuxiliaryProjectEditingHandler : ToolHandlerBase
    {
        public override string Name => "AuxiliaryProjectEditing";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Opens, saves or closes a session auxiliary project for live editing through normal project tools.",
            new JObject
            {
                ["method"] = "string (EDIT|SAVE|CLOSE)",
                ["projectName"] = "string (required for EDIT; optional for SAVE)",
                ["closeAfterSave"] = "bool? (SAVE only, default: true)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var method = (args.Value<string>("method") ?? string.Empty).Trim().ToUpperInvariant();
                var projectName = (args.Value<string>("projectName") ?? string.Empty).Trim();
                var sessionId = ToolHandlerUtilities.CurrentSessionGuid;
                return method switch
                {
                    "EDIT" => Task.FromResult(Edit(sessionId, projectName)),
                    "SAVE" => Task.FromResult(Save(
                        sessionId,
                        projectName,
                        args.Value<bool?>("closeAfterSave") ?? true)),
                    "CLOSE" => Task.FromResult(Close(sessionId)),
                    _ => Task.FromResult(ToolHandlerUtilities.Fail("method must be EDIT, SAVE or CLOSE."))
                };
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }

        private static ToolResponse Edit(string sessionId, string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                return ToolHandlerUtilities.Fail("projectName is required for EDIT.");

            var project = AuxiliaryProjectSessionStore.Edit(sessionId, projectName);
            return ProjectResponse(project, "editing", isOpen: true);
        }

        private static ToolResponse Save(
            string sessionId,
            string projectName,
            bool closeAfterSave)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                if (!AuxiliaryProjectSessionStore.TryGetActiveProject(sessionId, out var activeProject))
                    return ToolHandlerUtilities.Fail("No auxiliary project is currently open.");
                projectName = activeProject.ProjectName;
            }

            var (project, projectFile) = AuxiliaryProjectSessionStore.Save(sessionId, projectName);
            if (closeAfterSave)
                AuxiliaryProjectSessionStore.Close(sessionId);

            var response = ProjectResponse(project, "saved", isOpen: !closeAfterSave);
            response.Result["projectFile"] = projectFile;
            response.Result["closed"] = closeAfterSave;
            return response;
        }

        private static ToolResponse Close(string sessionId)
        {
            AuxiliaryProjectSessionStore.Close(sessionId);
            return ToolHandlerUtilities.Ok(new JObject
            {
                ["closed"] = true,
                ["currentProjectName"] = ToolHandlerUtilities.GetProject().ProjectName ?? string.Empty
            });
        }

        private static ToolResponse ProjectResponse(
            FlowBloxProject project,
            string operation,
            bool isOpen) =>
            ToolHandlerUtilities.Ok(new JObject
            {
                ["operation"] = operation,
                ["projectName"] = project.ProjectName ?? string.Empty,
                ["shortDescription"] = project.AssistantMetadata.ShortDescription ?? string.Empty,
                ["description"] = project.ProjectDescription ?? string.Empty,
                ["projectGuid"] = project.ProjectGuid,
                ["isOpen"] = isOpen
            });
    }
}
