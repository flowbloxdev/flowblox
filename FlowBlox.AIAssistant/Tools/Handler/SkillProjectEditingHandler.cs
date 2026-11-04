using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class SkillProjectEditingHandler : ToolHandlerBase
    {
        public override string Name => "SkillProjectEditing";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Opens, saves or closes a session skill project for live editing through normal project tools.",
            new JObject
            {
                ["method"] = "string (EDIT|SAVE|CLOSE)",
                ["skillProjectName"] = "string (required for EDIT; optional for SAVE)",
                ["closeAfterSave"] = "bool? (SAVE only, default: true)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var method = (args.Value<string>("method") ?? string.Empty).Trim().ToUpperInvariant();
                var skillProjectName = (args.Value<string>("skillProjectName") ?? string.Empty).Trim();
                var sessionId = ToolHandlerUtilities.CurrentSessionGuid;
                return method switch
                {
                    "EDIT" => Task.FromResult(Edit(sessionId, skillProjectName)),
                    "SAVE" => Task.FromResult(Save(
                        sessionId,
                        skillProjectName,
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

        private static ToolResponse Edit(string sessionId, string skillProjectName)
        {
            if (string.IsNullOrWhiteSpace(skillProjectName))
                return ToolHandlerUtilities.Fail("skillProjectName is required for EDIT.");

            var project = SkillProjectSessionStore.Edit(sessionId, skillProjectName);
            return ProjectResponse(project, "editing", isOpen: true);
        }

        private static ToolResponse Save(
            string sessionId,
            string skillProjectName,
            bool closeAfterSave)
        {
            if (string.IsNullOrWhiteSpace(skillProjectName))
            {
                if (!SkillProjectSessionStore.TryGetActiveProject(sessionId, out var activeProject))
                    return ToolHandlerUtilities.Fail("No skill project is currently open.");
                skillProjectName = activeProject.ProjectName;
            }

            var (project, projectFile) = SkillProjectSessionStore.Save(sessionId, skillProjectName);
            if (closeAfterSave)
                SkillProjectSessionStore.Close(sessionId);

            var response = ProjectResponse(project, "saved", isOpen: !closeAfterSave);
            response.Result["projectFile"] = projectFile;
            response.Result["closed"] = closeAfterSave;
            return response;
        }

        private static ToolResponse Close(string sessionId)
        {
            SkillProjectSessionStore.Close(sessionId);
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
                ["skillProjectName"] = project.ProjectName ?? string.Empty,
                ["shortDescription"] = project.AssistantMetadata.ShortDescription ?? string.Empty,
                ["description"] = project.ProjectDescription ?? string.Empty,
                ["projectGuid"] = project.ProjectGuid,
                ["isOpen"] = isOpen
            });
    }
}
