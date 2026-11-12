using FlowBlox.Core.Extensions;
using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Toolbox;
using FlowBlox.Core.Services;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.Generators
{
    [Display(Name = "AIPropertyValueGenerationStrategy_DisplayName", 
             Description = "AIPropertyValueGenerationStrategy_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSupportedTypes(typeof(BaseResultFlowBlock))]
    [FlowBloxUIGroup("Global_Groups_Default", 0, ControlAlignment.Top)]
    [FlowBloxUIGroup("AIPropertyValueGenerationStrategy_Groups_AdditionalSettings", 1)]
    public class AIPropertyValueGenerationStrategy : FlowBloxGenerationStrategyBase
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.creation, 16, SKColors.MediumPurple);

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.creation, 32, SKColors.MediumPurple);

        public AIPropertyValueGenerationStrategy()
            : base()
        {
            ApplyDefaultPromptTemplate();
            ApplyDefaultSystemInstruction();
        }

        public AIPropertyValueGenerationStrategy(BaseFlowBlock flowBlock)
            : base(flowBlock)
        {
            if (flowBlock is not BaseResultFlowBlock)
                throw new ArgumentException(nameof(flowBlock), $"The FlowBlock must be of type \"{typeof(BaseResultFlowBlock).Name}\".");

            ApplyDefaultPromptTemplate();
            ApplyDefaultSystemInstruction();
        }

        [Required]
        [Display(Name = "AIPropertyValueGenerationStrategy_Provider", Description = "AIPropertyValueGenerationStrategy_Provider_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleProviders),
            SelectionDisplayMember = nameof(ManagedObject.Name))]
        public AIProviderBase Provider { get; set; }

        [Required]
        [Display(Name = "AIPropertyValueGenerationStrategy_PromptTemplate", Description = "AIPropertyValueGenerationStrategy_PromptTemplate_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.AIPromptTemplates), UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxFieldSelection(AllowedFieldSelectionModes =
            FieldSelectionModes.Fields |
            FieldSelectionModes.ProjectProperties |
            FieldSelectionModes.Options |
            FieldSelectionModes.GenerationStrategyData)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true)]
        public string PromptTemplate { get; set; }

        [Display(Name = "AIPropertyValueGenerationStrategy_SystemInstruction", Description = "AIPropertyValueGenerationStrategy_SystemInstruction_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.AIPromptTemplates), UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox(MultiLine = true)]
        public string SystemInstruction { get; set; }

        [Display(Name = "AIPropertyValueGenerationStrategy_ModelOverride", Description = "AIPropertyValueGenerationStrategy_ModelOverride_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPropertyValueGenerationStrategy_Groups_AdditionalSettings", Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ModelOverride { get; set; }

        [Display(Name = "AIPropertyValueGenerationStrategy_Temperature", Description = "AIPropertyValueGenerationStrategy_Temperature_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPropertyValueGenerationStrategy_Groups_AdditionalSettings", Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public double? Temperature { get; set; } = null;

        [Display(Name = "AIPropertyValueGenerationStrategy_MaxTokens", Description = "AIPropertyValueGenerationStrategy_MaxTokens_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPropertyValueGenerationStrategy_Groups_AdditionalSettings", Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? MaxTokens { get; set; }

        [Display(Name = "AIPropertyValueGenerationStrategy_TimeoutSecondsOverride", Description = "AIPropertyValueGenerationStrategy_TimeoutSecondsOverride_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "AIPropertyValueGenerationStrategy_Groups_AdditionalSettings", Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? TimeoutSecondsOverride { get; set; }

        public List<AIProviderBase> GetPossibleProviders()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetManagedObjects<AIProviderBase>().ToList();
        }

        private void ApplyDefaultPromptTemplate()
        {
            if (!string.IsNullOrWhiteSpace(PromptTemplate))
                return;

            PromptTemplate = FlowBloxToolboxResourceProvider.GetToolboxElementContent(
                ToolboxConstants.AIPromptTemplatesCategory,
                ToolboxConstants.FlowBloxQuickUpdateConfiguratorName);
        }

        private void ApplyDefaultSystemInstruction()
        {
            if (!string.IsNullOrWhiteSpace(SystemInstruction))
                return;

            SystemInstruction = FlowBloxToolboxResourceProvider.GetToolboxElementContent(
                ToolboxConstants.AIPromptTemplatesCategory,
                ToolboxConstants.SystemFlowBloxQuickUpdatePromptTemplateName);
        }

        public override bool CanExecute(out Dictionary<FlowBloxTestDefinition, List<string>> testDefinitionToMessages, out List<string> messages)
        {
            testDefinitionToMessages = new Dictionary<FlowBloxTestDefinition, List<string>>();
            messages = new List<string>();

            if (Source is not BaseResultFlowBlock)
                messages.Add($"The source flow block must be of type \"{typeof(BaseResultFlowBlock).Name}\".");

            if (Provider == null)
                messages.Add("No AI provider specified.");

            if (string.IsNullOrWhiteSpace(PromptTemplate))
                messages.Add("No prompt template specified.");

            if (Source?.TestDefinitions == null || !Source.TestDefinitions.Any())
                messages.Add("At least one test case is required.");

            return !messages.Any();
        }

        public override object Execute(BaseRuntime runtime, Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (Provider == null || string.IsNullOrWhiteSpace(PromptTemplate))
                return null;
            if (runtime == null)
                throw new InvalidOperationException("No runtime available for AI property generation.");

            var resolvedPrompt = ResolveTemplate(PromptTemplate, testResults);
            var resolvedSystemInstruction = ResolveTemplate(SystemInstruction, testResults);
            var resolvedModel = ResolveTemplate(ModelOverride, testResults);

            var request = new AIRequest
            {
                Prompt = resolvedPrompt,
                SystemInstruction = resolvedSystemInstruction,
                Model = string.IsNullOrWhiteSpace(resolvedModel) ? null : resolvedModel,
                Temperature = this.Resolve(strategy => strategy.Temperature),
                MaxTokens = this.Resolve(strategy => strategy.MaxTokens),
                TimeoutSecondsOverride = this.Resolve(strategy => strategy.TimeoutSecondsOverride)
            };

            request.Meta["FlowBlock"] = Source?.Name;
            request.Meta["FlowBlockType"] = Source?.GetType().Name;
            request.Meta["GenerationStrategy"] = Name;

            var response = Provider
                .ExecuteAsync(runtime, request, runtime.GetCancellationToken())
                .GetAwaiter()
                .GetResult();

            if (!response.Success)
            {
                runtime.Report(
                    $"AI property generation failed for \"{Source?.Name}\".{Environment.NewLine}" +
                    $"Provider: {Provider?.Name ?? "n/a"} ({Provider?.ProviderType ?? "n/a"}){Environment.NewLine}" +
                    $"Error: {response.Error ?? "n/a"}",
                    FlowBloxLogLevel.Error);
                return null;
            }

            return response.Text?.Trim();
        }

        public override void Assign(object value)
        {
            if (Source == null)
                throw new InvalidOperationException("No source flow block available for FlowBlox Quick Update.");

            var instructionParserService = FlowBloxServiceLocator.Instance.GetService<IAiResponseInstructionParserService>();

            new FlowBloxQuickUpdateFormat(Source, instructionParserService).Apply(value?.ToString(), Provider);
        }

        private string ResolveTemplate(string template, Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            var resolved = ReplaceFieldTokensWithTestValues(template, testResults);
            resolved = FlowBloxFieldHelper.ReplaceFieldsInString(resolved);

            resolved = resolved.Replace(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, "TargetFlowBlockInputValues"), BuildTargetFlowBlockInputValuesText(testResults));
            resolved = resolved.Replace(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, "TestExpectations"), BuildTestExpectationsText(testResults));
            resolved = resolved.Replace(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, "TestResults"), BuildTestResultsText(testResults));
            resolved = resolved.Replace(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, "FlowBloxQuickUpdateSchema"), FlowBloxQuickUpdateSchemaProvider.BuildSchema(Source));
            resolved = resolved.Replace(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, "FlowBloxQuickUpdateFormat"), BuildQuickUpdateFormatText());
            resolved = resolved.Replace(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, "FlowBlockDescriptions"), BuildFlowBlockDescriptions());

            return resolved;
        }

        private string BuildTargetFlowBlockInputValuesText(Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (Source == null || testResults == null || testResults.Count == 0)
                return string.Empty;

            var referencedFields = Source.GetAssociatedFields()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.FullyQualifiedName))
                .GroupBy(x => x.FullyQualifiedName, StringComparer.Ordinal)
                .Select(x => x.First())
                .OrderBy(x => x.FullyQualifiedName, StringComparer.Ordinal)
                .ToList();

            if (referencedFields.Count == 0)
                return "No referenced input fields are available.";

            var sb = new StringBuilder();
            foreach (var testResult in testResults.OrderBy(x => x.Key.Name, StringComparer.Ordinal))
            {
                sb.AppendLine($"TestCase={testResult.Key.Name}");

                foreach (var field in referencedFields)
                {
                    var value = field.StringValue ?? string.Empty;
                    testResult.Value?.FieldValueAssignments?.TryGetValue(field.FullyQualifiedName, out value);
                    sb.AppendLine($"{field.FullyQualifiedName}={field.GetExternalValue(value ?? string.Empty)}");
                }

                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private string ReplaceFieldTokensWithTestValues(string input, Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var regex = new Regex(BaseFlowBlock.Regex_FullyQualifiedFieldNames);
            var tokens = regex.Matches(input)
                .Cast<Match>()
                .Select(x => x.Value)
                .Where(x => !x.StartsWith(FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.TestDefinition, string.Empty), StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            foreach (var token in tokens)
            {
                var field = FlowBloxRegistryProvider.GetRegistry().GetFieldElementOrNull(token);
                if (field?.IsPassword == true)
                {
                    input = input.Replace(token, GlobalConstants.HiddenSensitiveValue);
                    continue;
                }

                var values = new List<string>();
                foreach (var kvp in testResults.OrderBy(x => x.Key.Name))
                {
                    var testResult = kvp.Value;
                    if (testResult?.FieldValueAssignments == null)
                        continue;

                    if (testResult.FieldValueAssignments.TryGetValue(token, out var value))
                    {
                        values.Add($"[{kvp.Key.Name}] {value}");
                    }
                }

                if (values.Any())
                    input = input.Replace(token, string.Join(Environment.NewLine, values));
            }

            return input;
        }

        private static string BuildQuickUpdateFormatText()
        {
            return """
                   Return a JSON object with JsonContract set to "FlowBloxQuickUpdate".
                   Put the exact FlowBlox property names to update into the Properties object.
                   If a property is omitted, it remains unchanged.
                   Collections must always be returned completely; existing collection items are replaced by the provided list.
                   For enum values, return the enum name as a string.
                   Example: { "JsonContract": "FlowBloxQuickUpdate", "Properties": { "PropertyName": "value", "EnumProperty": "EnumValue" } }
                   """;
        }

        private string BuildTestResultsText(Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (testResults == null || testResults.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var kvp in testResults.OrderBy(x => x.Key.Name))
            {
                sb.AppendLine($"TestCase={kvp.Key.Name}");
                sb.AppendLine($"Success={kvp.Value?.Success}");
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        private string BuildTestExpectationsText(Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (Source == null || testResults == null || testResults.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            foreach (var kvp in testResults.OrderBy(x => x.Key.Name))
            {
                var testDefinition = kvp.Key;

                if (testDefinition?.Entries == null)
                    throw new InvalidOperationException($"TestCase \"{testDefinition?.Name ?? "n/a"}\" has no entries.");

                var testEntry = testDefinition.Entries.SingleOrDefault(x => x.FlowBlock == Source);
                if (testEntry == null)
                    throw new InvalidOperationException($"TestCase \"{testDefinition.Name}\" has no test entry for flow block \"{Source?.Name ?? "n/a"}\".");

                if (testEntry.FlowBloxTestConfigurations == null || !testEntry.FlowBloxTestConfigurations.Any())
                    throw new InvalidOperationException(
                        $"TestCase \"{testDefinition.Name}\" has no test configurations for flow block \"{Source?.Name ?? "n/a"}\". " +
                        "Cannot generate AI prompt without expectations.");

                sb.AppendLine($"TestCase={testDefinition.Name}");

                var counter = 1;

                var hasAnyExpectationAnchor = false;

                foreach (var config in testEntry.FlowBloxTestConfigurations)
                {
                    sb.AppendLine($"{counter}. Expectation");
                    sb.AppendLine($"Field={config.FieldElement?.FullyQualifiedName ?? "n/a"}");

                    if (!string.IsNullOrWhiteSpace(config.UserInput) &&
                        config.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue)
                    {
                        sb.AppendLine($"ExpectedValue={config.UserInput}");
                        hasAnyExpectationAnchor = true;
                    }

                    if (config.ExpectationConditions != null)
                    {
                        foreach (var condition in config.ExpectationConditions)
                        {
                            var targetText = condition.ExpectationConditionTarget.ToString();
                            if (condition.ExpectationConditionTarget == ExpectationConditionTarget.ValueAtIndex)
                            {
                                targetText = $"{targetText}({condition.Index})";
                                hasAnyExpectationAnchor = true;
                            }
                            else if (condition.ExpectationConditionTarget == ExpectationConditionTarget.AnyValue)
                            {
                                hasAnyExpectationAnchor = true;
                            }

                            sb.AppendLine($"{targetText} {condition.Operator} {condition.Value}");
                        }
                    }

                    sb.AppendLine();
                    counter++;
                }

                if (!hasAnyExpectationAnchor)
                {
                    throw new InvalidOperationException(
                        $"TestCase \"{testDefinition.Name}\" provides no usable expectation anchor for flow block \"{Source?.Name ?? "n/a"}\". " +
                        "Provide either SelectionMode=UserInput_ExpectedValue with a non-empty value, " +
                        "or an expectation condition targeting AnyValue or ValueAtIndex.");
                }
            }

            return sb.ToString().Trim();
        }

        private string BuildFlowBlockDescriptions()
        {
            if (Source == null)
                return string.Empty;

            var description = FlowBloxComponentHelper.GetDescription(Source) ?? string.Empty;

            var specialExplanations = Source.GetType()
                .GetCustomAttributes(typeof(FlowBloxSpecialExplanationAttribute), inherit: true)
                .OfType<FlowBloxSpecialExplanationAttribute>()
                .Select(x => x.GetResolvedSpecialExplanation())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "Description: n/a" : $"Description: {description}");

            if (specialExplanations.Count > 0)
            {
                sb.AppendLine("SpecialExplanations:");
                foreach (var explanation in specialExplanations)
                {
                    sb.AppendLine($"- {explanation}");
                }
            }

            return sb.ToString().Trim();
        }
    }
}
