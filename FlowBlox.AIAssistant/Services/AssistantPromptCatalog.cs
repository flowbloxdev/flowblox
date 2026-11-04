using System.Reflection;
using System.Text;
using FlowBlox.Core.Constants;

namespace FlowBlox.AIAssistant.Services
{
    internal static class AssistantPromptCatalog
    {
        public const string SystemMessageKey = "system_message";
        public const string SessionBootstrapKey = "session_bootstrap";
        public const string FlowLogicKey = "explaining_flow_logic";
        public const string IterationContextKey = "explaining_iteration_context";
        public const string FlowBlocksManagingObjectKey = "explaining_flow_blocks_managing_an_object";
        public const string EditAndDeleteKey = "explaining_edit_and_delete";
        public const string NamingConventionsKey = "naming_conventions";
        public const string ConditionsAndDecisionsKey = "explaining_conditions_and_decisions";
        public const string FlowOrganizationPatternsKey = "explaining_flow_organization_patterns";
        public const string DebuggingKey = "explaining_debugging";
        public const string InputFilesKey = "explaining_input_files";
        public const string SpecialTestDefinitionsKey = "explaining_test_definitions";
        public const string SpecialTestDefinitionsDeepDiveKey = "explaining_test_definitions_deep_dive";
        public const string SpecialGeneratorsKey = "explaining_generators";
        public const string SpecialTestDrivenUnknownResourcesKey = "explaining_test_driven_unknown_resources";
        public const string SpecialModifiersAndFieldValidatorsKey = "explaining_modifiers_and_field_validators";
        public const string SpecialOnnxGenAIModelsKey = "explaining_onnx_genai_models";
        public const string SpecialOnnxQAModelsKey = "explaining_onnx_qa_models";
        public const string SpecialFlowRecursionKey = "explaining_flow_recursion";
        public const string SpecialExtensionsKey = "explaining_extensions";
        public const string SpecialProjectSpaceKey = "explaining_project_space";
        public const string SpecialTaskSchedulingKey = "explaining_task_scheduling";
        public const string SpecialUserInterfaceKey = "explaining_user_interface";
        public const string SpecialSkillsKey = "explaining_skills";
        public const string VersionNotesKey = "explaining_version_notes";

        private static readonly IReadOnlyDictionary<string, PromptEntryDefinition> Definitions =
            new Dictionary<string, PromptEntryDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [SystemMessageKey] = new PromptEntryDefinition(
                    SystemMessageKey,
                    "System Message",
                    "FlowBlox.AIAssistant.Prompts.SystemMessage.txt",
                    "Core response contract and finalization rules.",
                    true),
                [SessionBootstrapKey] = new PromptEntryDefinition(
                    SessionBootstrapKey,
                    "Session Bootstrap",
                    "FlowBlox.AIAssistant.Prompts.SessionBootstrap.txt",
                    "Editing rules, tool usage policy, and workflow guardrails.",
                    true),
                [FlowLogicKey] = new PromptEntryDefinition(
                    FlowLogicKey,
                    "Explaining FlowLogic",
                    "FlowBlox.AIAssistant.Prompts.ExplainingFlowLogic.txt",
                    "Normal flow execution order, dataset propagation, and iterative behavior.",
                    true),
                [IterationContextKey] = new PromptEntryDefinition(
                    IterationContextKey,
                    "Explaining IterationContext",
                    "FlowBlox.AIAssistant.Prompts.ExplainingIterationContext.txt",
                    "Important for understanding FlowBlox flow execution logic.",
                    true),
                [FlowBlocksManagingObjectKey] = new PromptEntryDefinition(
                    FlowBlocksManagingObjectKey,
                    "Explaining FlowBlocks Managing an Object",
                    "FlowBlox.AIAssistant.Prompts.ExplainingFlowBlocksManagingAnObject.txt",
                    "How object-managing flow blocks define object lifecycle per dataset or per run.",
                    true),
                [EditAndDeleteKey] = new PromptEntryDefinition(
                    EditAndDeleteKey,
                    "Explaining Edit and Delete",
                    "FlowBlox.AIAssistant.Prompts.ExplainingEditAndDelete.txt",
                    "How to update, link/unlink, and delete safely with automatic dependency handling.",
                    true),
                [NamingConventionsKey] = new PromptEntryDefinition(
                    NamingConventionsKey,
                    "Naming Conventions",
                    "FlowBlox.AIAssistant.Prompts.NamingConventions.txt",
                    "Consistent naming rules for FlowBlocks and result-field descriptors.",
                    true),
                [ConditionsAndDecisionsKey] = new PromptEntryDefinition(
                    ConditionsAndDecisionsKey,
                    "Explaining Conditions and Decisions",
                    "FlowBlox.AIAssistant.Prompts.ExplainingConditionsAndDecisions.txt",
                    "How to choose between RequiredFields, ActivationConditions, and DecisionFlowBlock value selection.",
                    true),
                [FlowOrganizationPatternsKey] = new PromptEntryDefinition(
                    FlowOrganizationPatternsKey,
                    "Explaining Flow Organization Patterns",
                    "FlowBlox.AIAssistant.Prompts.ExplainingFlowOrganizationPatterns.txt",
                    "How to structure flows with downstream vs. stack patterns for readability and maintainability.",
                    true),
                [DebuggingKey] = new PromptEntryDefinition(
                    DebuggingKey,
                    "Explaining Debugging",
                    "FlowBlox.AIAssistant.Prompts.ExplainingDebugging.txt",
                    "Compact default guidance for debug/test usage, result windowing, and field-value limits.",
                    true),
                [InputFilesKey] = new PromptEntryDefinition(
                    InputFilesKey,
                    "Explaining Managed Input Files",
                    "FlowBlox.AIAssistant.Prompts.ExplainingInputFiles.txt",
                    "Managed input files, schemas, mock data, helper scripts, or file commands",
                    false),
                [SpecialTestDefinitionsKey] = new PromptEntryDefinition(
                    SpecialTestDefinitionsKey,
                    "Explaining Test Definitions",
                    "FlowBlox.AIAssistant.Prompts.ExplainingTestDefinitions.txt",
                    "Creating, linking, or configuring test definitions",
                    false),
                [SpecialTestDefinitionsDeepDiveKey] = new PromptEntryDefinition(
                    SpecialTestDefinitionsDeepDiveKey,
                    "Explaining Test Definitions (Deep Dive)",
                    "FlowBlox.AIAssistant.Prompts.ExplainingTestDefinitionsDeepDive.txt",
                    "Advanced test-definition use cases and scope strategies",
                    false),
                [SpecialGeneratorsKey] = new PromptEntryDefinition(
                    SpecialGeneratorsKey,
                    "Explaining Generators",
                    "FlowBlox.AIAssistant.Prompts.ExplainingGenerators.txt",
                    "Configuring generation strategies from test results",
                    false),
                [SpecialTestDrivenUnknownResourcesKey] = new PromptEntryDefinition(
                    SpecialTestDrivenUnknownResourcesKey,
                    "Test-Driven Approach for Unknown/Inaccessible Resources",
                    "FlowBlox.AIAssistant.Prompts.ExplainingTestDrivenUnknownResources.txt",
                    "Iterative/test-driven flows for unknown or inaccessible content",
                    false),
                [SpecialModifiersAndFieldValidatorsKey] = new PromptEntryDefinition(
                    SpecialModifiersAndFieldValidatorsKey,
                    "Explaining Modifiers and Field Validators",
                    "FlowBlox.AIAssistant.Prompts.ExplainingModifiersAndFieldValidators.txt",
                    "Field cleanup, transformation, validation, or filtering",
                    false),
                [SpecialOnnxGenAIModelsKey] = new PromptEntryDefinition(
                    SpecialOnnxGenAIModelsKey,
                    "Explaining ONNX Runtime GenAI Models",
                    "FlowBlox.AIAssistant.Prompts.ExplainingOnnxGenAIModels.txt",
                    "Obtaining or configuring ONNX Runtime GenAI models",
                    false),
                [SpecialOnnxQAModelsKey] = new PromptEntryDefinition(
                    SpecialOnnxQAModelsKey,
                    "Explaining ONNX Question Answering Models",
                    "FlowBlox.AIAssistant.Prompts.ExplainingOnnxQAModels.txt",
                    "Selecting, exporting, or configuring ONNX question-answering models",
                    false),
                [SpecialFlowRecursionKey] = new PromptEntryDefinition(
                    SpecialFlowRecursionKey,
                    "Explaining Flow Recursion",
                    "FlowBlox.AIAssistant.Prompts.ExplainingFlowRecursion.txt",
                    "Recursion, paging, polling, or retry loops",
                    false),
                [SpecialExtensionsKey] = new PromptEntryDefinition(
                    SpecialExtensionsKey,
                    "Explaining Extensions",
                    "FlowBlox.AIAssistant.Prompts.ExplainingExtensions.txt",
                    "Developing, debugging, loading, or publishing FlowBlox extensions",
                    false),
                [SpecialProjectSpaceKey] = new PromptEntryDefinition(
                    SpecialProjectSpaceKey,
                    "Explaining Project Space",
                    "FlowBlox.AIAssistant.Prompts.ExplainingProjectSpace.txt",
                    "Project Space, project sharing, stable versions, or remote execution",
                    false),
                [SpecialTaskSchedulingKey] = new PromptEntryDefinition(
                    SpecialTaskSchedulingKey,
                    "Explaining Task Scheduling and Service Execution",
                    "FlowBlox.AIAssistant.Prompts.ExplainingTaskScheduling.txt",
                    "Scheduled, recurring, or continuous service execution",
                    false),
                [SpecialUserInterfaceKey] = new PromptEntryDefinition(
                    SpecialUserInterfaceKey,
                    "Explaining User Interface",
                    "FlowBlox.AIAssistant.Prompts.ExplainingUserInterface.txt",
                    "FlowBlox UI panels, menus, actions, or manual workflows",
                    false),
                [SpecialSkillsKey] = new PromptEntryDefinition(
                    SpecialSkillsKey,
                    "Explaining Skills",
                    "FlowBlox.AIAssistant.Prompts.ExplainingSkills.txt",
                    "Discovering or using Skills (AI Assistant capabilities), or creating or editing the Skill Projects that implement them",
                    false),
                [VersionNotesKey] = new PromptEntryDefinition(
                    VersionNotesKey,
                    "Version Notes",
                    "FlowBlox.AIAssistant.Prompts.ExplainingVersionNotes.txt",
                    "Current release limitations and implementation-status hints for AI Assistant editing.",
                    true)
            };

        private static readonly Lazy<IReadOnlyDictionary<string, PromptEntry>> Entries = new(LoadEntries, isThreadSafe: true);

        public static string? GetPromptContentOrNull(string key)
        {
            if (!Entries.Value.TryGetValue(key, out var entry))
                return null;

            return entry.Content;
        }

        public static IReadOnlyList<PromptEntry> GetAllEntries()
        {
            return Entries.Value.Values
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool TryGetEntry(string key, out PromptEntry? entry)
        {
            if (Entries.Value.TryGetValue(key, out var found))
            {
                entry = found;
                return true;
            }

            entry = null;
            return false;
        }

        private static IReadOnlyDictionary<string, PromptEntry> LoadEntries()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var result = new Dictionary<string, PromptEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var definition in Definitions.Values)
            {
                var content = ReadEmbeddedTextOrNull(assembly, definition.ResourceName);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var normalized = ReplaceGlobalUrlTokens(content.Replace("\r\n", "\n").Trim());
                if (normalized.Length == 0)
                    continue;

                if (definition.Key != SystemMessageKey && definition.Key != SessionBootstrapKey)
                    normalized = EnsureTitleHeader(normalized, definition.Title);

                result[definition.Key] = new PromptEntry(
                    definition.Key,
                    definition.Title,
                    definition.Description,
                    normalized,
                    definition.IsIncludedInInitialPrompt);
            }

            return result;
        }

        private static string EnsureTitleHeader(string content, string title)
        {
            if (content.StartsWith("# ", StringComparison.Ordinal))
                return content;

            var lineBreakIndex = content.IndexOf('\n');
            var firstLine = lineBreakIndex < 0 ? content : content[..lineBreakIndex];
            var remainder = lineBreakIndex < 0 ? string.Empty : content[(lineBreakIndex + 1)..];
            var remainderStartsWithContinuation = remainder.TrimStart().StartsWith("(", StringComparison.Ordinal);
            var firstLineLooksLikeTitle = firstLine.EndsWith(":", StringComparison.Ordinal) ||
                                          (!firstLine.EndsWith(".", StringComparison.Ordinal) &&
                                           !firstLine.EndsWith("?", StringComparison.Ordinal) &&
                                           !firstLine.EndsWith("!", StringComparison.Ordinal) &&
                                           !remainderStartsWithContinuation);
            if (firstLineLooksLikeTitle)
                return string.IsNullOrWhiteSpace(remainder)
                    ? $"# {title}"
                    : $"# {title}\n{remainder}";

            return $"# {title}\n\n{content}";
        }

        private static string ReplaceGlobalUrlTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;

            var result = text;
            foreach (var url in GlobalUrls.GetAll())
                result = result.Replace("{{" + url.Key + "}}", url.Value, StringComparison.Ordinal);

            var currentDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var serviceInstallerPath = string.IsNullOrWhiteSpace(systemDirectory)
                ? "sc.exe"
                : Path.Combine(systemDirectory, "sc.exe");

            result = result.Replace("{{CURRENT_DIRECTORY}}", currentDirectory, StringComparison.Ordinal);
            result = result.Replace("{{CURRENT_DIRECTORY_JSON}}", currentDirectory.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal);
            result = result.Replace("{{SERVICE_INSTALLER_PATH}}", serviceInstallerPath, StringComparison.Ordinal);

            return result;
        }

        private static string? ReadEmbeddedTextOrNull(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private sealed record PromptEntryDefinition(
            string Key,
            string Title,
            string ResourceName,
            string Description,
            bool IsIncludedInInitialPrompt);

        internal sealed record PromptEntry(
            string Key,
            string Title,
            string Description,
            string Content,
            bool IsIncludedInInitialPrompt);
    }
}
