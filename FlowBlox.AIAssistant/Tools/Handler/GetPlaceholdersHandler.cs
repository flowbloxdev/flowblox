using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Placeholders.GenerationStrategy;
using FlowBlox.Core.Provider.Placeholders.InputFile;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetPlaceholdersHandler : ToolHandlerBase
    {
        public override string Name => "GetPlaceholders";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns available placeholders for string properties and field references for supported scalar properties. " +
            "Includes field placeholders ($FlowBlock::FieldName / $User::FieldName), $Project:: / $Options:: placeholders, " +
            "user-managed $Vault:: references (metadata only, never stored values), $InputFile::Path / $InputFile::RelativePath and $GenerationStrategy:: placeholders.",
            new JObject());

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var registry = ToolHandlerUtilities.GetRegistry();
            var project = ToolHandlerUtilities.GetProject();
            var options = FlowBloxOptions.GetOptionInstance();

            var fieldPlaceholders = registry.GetFieldElements()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.FullyQualifiedName))
                .GroupBy(x => x.FullyQualifiedName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.UserField ? 0 : 1)
                .ThenBy(x => x.FullyQualifiedName, StringComparer.OrdinalIgnoreCase)
                .Select(ToFieldPlaceholderInfo)
                .ToList();

            var projectPlaceholders = project.GetProjectPropertyElements()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Placeholder))
                .OrderBy(x => x.Placeholder, StringComparer.OrdinalIgnoreCase)
                .Select(ToProjectPlaceholderInfo)
                .ToList();

            var optionPlaceholders = options.GetOptions()
                .Where(x => x != null && x.IsPlaceholderEnabled)
                .Select(ToOptionPlaceholderInfo)
                .OrderBy(x => x.Value<string>("placeholder"), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var inputFilePlaceholders = FlowBloxInputFilePlaceholderProvider.GetElements(project.ProjectInputDirectory)
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Placeholder))
                .OrderBy(x => x.Placeholder, StringComparer.OrdinalIgnoreCase)
                .Select(ToInputFilePlaceholderInfo)
                .ToList();

            var generationStrategyPlaceholders = FlowBloxGenerationStrategyPlaceholderProvider.GetElements()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Placeholder))
                .OrderBy(x => x.Placeholder, StringComparer.OrdinalIgnoreCase)
                .Select(ToGenerationStrategyPlaceholderInfo)
                .ToList();

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["vaultPlaceholders"] = new JArray(FlowBlox.Core.Util.Vault.FlowBloxVault.GetReferences().Select(entry => new JObject
                {
                    ["name"] = entry.Name,
                    ["description"] = entry.Description,
                    ["placeholder"] = entry.Reference,
                    ["isPassword"] = entry.IsPassword
                })),
                ["fieldPlaceholders"] = new JArray(fieldPlaceholders),
                ["projectPlaceholders"] = new JArray(projectPlaceholders),
                ["optionPlaceholders"] = new JArray(optionPlaceholders),
                ["inputFilePlaceholders"] = new JArray(inputFilePlaceholders),
                ["generationStrategyPlaceholders"] = new JArray(generationStrategyPlaceholders),
                ["summary"] = new JObject
                {
                    ["fieldPlaceholderCount"] = fieldPlaceholders.Count,
                    ["userFieldCount"] = fieldPlaceholders.Count(x => x.Value<bool?>("isUserField") == true),
                    ["runtimeFieldCount"] = fieldPlaceholders.Count(x => x.Value<bool?>("isUserField") != true),
                    ["projectPlaceholderCount"] = projectPlaceholders.Count,
                    ["optionPlaceholderCount"] = optionPlaceholders.Count,
                    ["inputFilePlaceholderCount"] = inputFilePlaceholders.Count,
                    ["generationStrategyPlaceholderCount"] = generationStrategyPlaceholders.Count
                },
                ["hint"] = "Use placeholders in string properties or Skill inputParameters; use field references for supported scalar properties. Vault references are allowed only in project Input field values and Skill inputParameters; never in Memory fields or directly in FlowBlock properties."
            }));
        }

        private static JObject ToFieldPlaceholderInfo(FieldElement field)
        {
            var isUserField = field.UserField;

            return new JObject
            {
                ["placeholder"] = field.FullyQualifiedName,
                ["fullyQualifiedFieldName"] = field.FullyQualifiedName,
                ["fieldName"] = field.Name ?? string.Empty,
                ["isUserField"] = isUserField,
                ["userFieldType"] = isUserField ? field.UserFieldType.ToString() : string.Empty,
                ["originFlowBlockName"] = isUserField ? string.Empty : field.FlowBlockName ?? string.Empty,
                ["origin"] = isUserField ? "UserField" : "FlowBlockResultField",
                ["fieldType"] = field.FieldType?.FieldType.ToString() ?? string.Empty
            };
        }

        private static JObject ToProjectPlaceholderInfo(FlowBloxProjectPropertyElement propertyElement)
        {
            return new JObject
            {
                ["placeholder"] = propertyElement.Placeholder,
                ["key"] = propertyElement.Key ?? string.Empty,
                ["displayName"] = propertyElement.DisplayName ?? string.Empty,
                ["description"] = propertyElement.Description ?? string.Empty,
                ["origin"] = "ProjectProperty"
            };
        }

        private static JObject ToOptionPlaceholderInfo(OptionElement optionElement)
        {
            return new JObject
            {
                ["placeholder"] = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Options, optionElement.Name),
                ["name"] = optionElement.Name ?? string.Empty,
                ["displayName"] = optionElement.DisplayName ?? optionElement.Name ?? string.Empty,
                ["description"] = optionElement.Description ?? string.Empty,
                ["optionType"] = optionElement.Type.ToString(),
                ["origin"] = "Option"
            };
        }

        private static JObject ToInputFilePlaceholderInfo(FlowBloxInputFilePlaceholderElement inputFileElement)
        {
            return new JObject
            {
                ["placeholder"] = inputFileElement.Placeholder,
                ["key"] = inputFileElement.Key ?? string.Empty,
                ["displayName"] = inputFileElement.DisplayName ?? string.Empty,
                ["description"] = inputFileElement.Description ?? string.Empty,
                ["origin"] = "InputFile"
            };
        }

        private static JObject ToGenerationStrategyPlaceholderInfo(FlowBloxGenerationStrategyPlaceholderElement generationStrategyElement)
        {
            return new JObject
            {
                ["placeholder"] = generationStrategyElement.Placeholder,
                ["key"] = generationStrategyElement.Key ?? string.Empty,
                ["displayName"] = generationStrategyElement.DisplayName ?? string.Empty,
                ["description"] = generationStrategyElement.Description ?? string.Empty,
                ["origin"] = "GenerationStrategyData"
            };
        }
    }
}
