using System.Diagnostics;
using System.Reflection;
using System.Text;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Compression;
using FlowBlox.Core.Util.DistributedData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Builder
{
    internal static class AssistantPromptBuilder
    {
        public static string BuildSystemPrompt()
        {
            var prompt = AssistantPromptCatalog.GetPromptContentOrNull(AssistantPromptCatalog.SystemMessageKey);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new InvalidOperationException(
                    $"Required assistant system prompt '{AssistantPromptCatalog.SystemMessageKey}' is missing or empty.");
            }

            return ReplaceRuntimePromptTokens(prompt);
        }

        public static string BuildSessionBootstrapPrompt(IReadOnlyList<ToolDefinition> toolDefinitions, bool runtimeBindingsReset = false)
        {
            var rootCategories = FlowBlockCategory.GetAll()
                .Where(x => x.ParentCategory == null)
                .Select(x => x.DisplayName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            var template = AssistantPromptCatalog.GetPromptContentOrNull(AssistantPromptCatalog.SessionBootstrapKey);
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException(
                    $"Required assistant bootstrap prompt '{AssistantPromptCatalog.SessionBootstrapKey}' is missing or empty.");
            }

            return ReplaceRuntimePromptTokens(template)
                .Replace("{{ROOT_CATEGORIES}}", string.Join(", ", rootCategories), StringComparison.Ordinal)
                .Replace("{{CENTRAL_GUIDELINES}}", BuildCentralGuidelinesText(), StringComparison.Ordinal)
                .Replace("{{EXPLANATION_ROUTING}}", BuildExplanationRoutingText(), StringComparison.Ordinal)
                .Replace("{{STANDARD_SKILL_PROJECTS}}", BuildStandardSkillProjectsText(), StringComparison.Ordinal)
                .Replace("{{COMPACT_TYPE_ALIASES}}", AiAssistantTypeAliasHelper.AliasSummary, StringComparison.Ordinal)
                .Replace("{{AVAILABLE_TOOLS}}", BuildToolDefinitionsText(toolDefinitions), StringComparison.Ordinal)
                + $"\nApplication runtime started at: {AssistantSessionContentStore.ApplicationStartedAt:O}."
                + (runtimeBindingsReset
                    ? " Runtime system-message promotions were reset on conversation restore. Older history/summary claims about promoted types or explanations do not establish loaded state; retrieve needed content again."
                    : string.Empty);
        }

        public static string BuildInitialUserPrompt(
            string userPrompt,
            string? projectJson,
            ProjectAttachmentInformation projectAttachmentInformation,
            string? activeSkillProjectName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("User prompt:");
            sb.AppendLine(userPrompt);
            sb.AppendLine();

            AppendSkillProjectContext(sb, activeSkillProjectName);

            var attachmentReasonText = BuildProjectAttachmentInformationText(projectAttachmentInformation);
            if (!string.IsNullOrWhiteSpace(attachmentReasonText))
            {
                sb.AppendLine("Project attachment note:");
                sb.AppendLine(attachmentReasonText);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(projectJson))
            {
                sb.AppendLine("Current project JSON:");
                sb.AppendLine(projectJson);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string BuildToolApiResponsePrompt(
            IReadOnlyList<string> toolTranscript,
            string? activeSkillProjectName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tool execution updates since last assistant request:");

            if (toolTranscript == null || toolTranscript.Count == 0)
            {
                sb.AppendLine("[]");
            }
            else
            {
                foreach (var item in toolTranscript)
                    sb.AppendLine(item);
            }

            sb.AppendLine();
            AppendSkillProjectContext(sb, activeSkillProjectName);

            return sb.ToString().TrimEnd();
        }

        private static void AppendSkillProjectContext(StringBuilder sb, string? projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                sb.AppendLine("Skill project context: none; project tools target the main project.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"Skill project context: editing '{projectName}'; all project tools target this skill project.");
            sb.AppendLine();
        }

        private static string BuildToolDefinitionsText(IReadOnlyList<ToolDefinition> toolDefinitions)
        {
            var sb = new StringBuilder();
            foreach (var tool in toolDefinitions ?? Array.Empty<ToolDefinition>())
            {
                sb.Append("- ");
                sb.Append(tool.Name);
                sb.Append(": ");
                sb.Append(tool.Description);
                if (tool.ArgumentsSchema?.HasValues == true)
                {
                    sb.Append(" args=");
                    sb.Append(tool.ArgumentsSchema.ToString(Formatting.None));
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildProjectAttachmentInformationText(ProjectAttachmentInformation projectAttachmentReason)
        {
            return projectAttachmentReason switch
            {
                ProjectAttachmentInformation.InitialTransmission =>
                    "The current project JSON is attached because it has not yet been provided in this conversation. Treat it as the initial project state.",
                ProjectAttachmentInformation.ProjectChangedSinceLastConversation =>
                    "The current project JSON is attached because the project changed since the last conversation state was saved. Treat it as the latest project state.",
                ProjectAttachmentInformation.ProjectUnchangedSinceLastConversation =>
                    "The current project JSON is omitted because the project has not changed since the last conversation state was saved. Continue using the latest project state already available in this conversation.",
                ProjectAttachmentInformation.ProjectJsonDisabledInitialState =>
                    "The current project JSON is omitted because automatic project JSON attachment is disabled. No project hash/state is stored for this conversation yet. Request the project JSON with the available project tool if project context is needed.",
                ProjectAttachmentInformation.ProjectJsonDisabledProjectChanged =>
                    "The current project JSON is omitted because automatic project JSON attachment is disabled. The project changed since the last saved conversation state. Request the project JSON with the available project tool if the current project state is needed.",
                ProjectAttachmentInformation.ProjectJsonDisabledProjectUnchanged =>
                    "The current project JSON is omitted because automatic project JSON attachment is disabled. The project has not changed since the last saved conversation state. Continue using the latest project state already available in this conversation, if one was requested earlier.",
                _ => string.Empty
            };
        }

        private static string BuildExplanationRoutingText()
        {
            var entries = AssistantPromptCatalog.GetAllEntries()
                .Where(x => !x.IsIncludedInInitialPrompt && !string.IsNullOrWhiteSpace(x.Description))
                .Select(x => $"- {x.Description} -> `{x.Key}`")
                .ToList();

            return entries.Count == 0 ? "- None" : string.Join("\n", entries);
        }

        private static string BuildStandardSkillProjectsText()
        {
            var dataType = DistributedDataCatalog.GetDataTypes()
                .SingleOrDefault(x => string.Equals(
                    x.Name,
                    DistributedDataCatalog.SkillProjectType,
                    StringComparison.OrdinalIgnoreCase));
            if (dataType == null)
                return "- None";

            var entries = dataType.FileNames
                .Select(fileName => DistributedDataCatalog.ResolvePath(
                    DistributedDataCatalog.SkillProjectType,
                    fileName))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new
                {
                    Path = path!,
                    Project = JObject.Parse(File.ReadAllText(path!))
                })
                .Select(x => new
                {
                    ProjectName = x.Project.Value<string>(nameof(FlowBloxProject.ProjectName)) ?? Path.GetFileNameWithoutExtension(x.Path),
                    ShortDescription = x.Project[nameof(FlowBloxProject.AssistantMetadata)]?.Value<string>(nameof(AssistantMetadata.ShortDescription)) ?? string.Empty
                })
                .OrderBy(metadata => metadata.ProjectName, StringComparer.OrdinalIgnoreCase)
                .Select(metadata => $"- {metadata.ProjectName}: {metadata.ShortDescription}")
                .ToList();

            return entries.Count == 0 ? "- None" : string.Join("\n", entries);
        }

        private static string BuildCentralGuidelinesText()
        {
            var sections = new List<string>();

            AddPromptSection(sections, "Topic: FlowLogic", AssistantPromptCatalog.FlowLogicKey);
            AddPromptSection(sections, "Topic: IterationContext / Flow", AssistantPromptCatalog.IterationContextKey);
            AddPromptSection(sections, "Topic: FlowBlocks Managing an Object", AssistantPromptCatalog.FlowBlocksManagingObjectKey);
            AddPromptSection(sections, "Topic: Update / Delete Handling", AssistantPromptCatalog.EditAndDeleteKey);
            AddPromptSection(sections, "Topic: Naming Conventions", AssistantPromptCatalog.NamingConventionsKey);
            AddPromptSection(sections, "Topic: Conditions and Decisions", AssistantPromptCatalog.ConditionsAndDecisionsKey);
            AddPromptSection(sections, "Topic: Flow Organization Patterns", AssistantPromptCatalog.FlowOrganizationPatternsKey);
            AddPromptSection(sections, "Topic: Debugging", AssistantPromptCatalog.DebuggingKey);
            AddPromptSection(sections, "Topic: Version Notes", AssistantPromptCatalog.VersionNotesKey);

            if (sections.Count == 0)
                return "No central guidelines available.";

            return string.Join("\n\n", sections);
        }

        private static void AddPromptSection(List<string> sections, string title, string key)
        {
            var content = AssistantPromptCatalog.GetPromptContentOrNull(key);
            if (!string.IsNullOrWhiteSpace(content))
                sections.Add(title + "\n" + ReplaceRuntimePromptTokens(content).Trim());
        }

        private static string ReplaceRuntimePromptTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;

            var result = text.Replace("{{FLOWBLOX_VERSION}}", GetFlowBloxApplicationVersion(), StringComparison.Ordinal);
            foreach (var url in GlobalUrls.GetAll())
                result = result.Replace("{{" + url.Key + "}}", url.Value, StringComparison.Ordinal);

            return result;
        }

        private static string GetFlowBloxApplicationVersion()
        {
            try
            {
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly == null)
                    return "unknown";

                var location = entryAssembly.Location;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    var productVersion = FileVersionInfo.GetVersionInfo(location).ProductVersion;
                    if (!string.IsNullOrWhiteSpace(productVersion))
                        return productVersion;
                }

                return entryAssembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to resolve the FlowBlox application version for the AI Assistant prompt.",
                    ex);
                return "unknown";
            }
        }
    }
}
