using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using FlowBlox.SequenceDetection;
using FlowBlox.SequenceDetection.Model;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Generators
{
    [Display(Name = "SequenceDetectionGenerationStrategy_DisplayName", Description = "SequenceDetectionGenerationStrategy_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSupportedTypes(typeof(SequenceDetectionFlowBlock))]
    public class SequenceDetectionGenerationStrategy : FlowBloxPipeGenerationStrategyBase
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_search, 16, SKColors.SteelBlue);

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_search, 32, SKColors.SteelBlue);

        private static readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private SequenceDetectionFlowBlock GetSequenceDetectionFlowBlock() => (SequenceDetectionFlowBlock)Source;

        public SequenceDetectionGenerationStrategy() : base()
        {

        }

        public SequenceDetectionGenerationStrategy(BaseFlowBlock flowBlock) : base(flowBlock)
        {
            if (flowBlock is not SequenceDetectionFlowBlock)
                throw new ArgumentException(nameof(flowBlock), $"The FlowBlock must be of type \"{typeof(SequenceDetectionFlowBlock).Name}\".");
        }

        public override void Assign(object value)
        {
            var sequenceDetectionFlowBlock = GetSequenceDetectionFlowBlock();
            sequenceDetectionFlowBlock.SequenceDetectionPattern = JsonConvert.SerializeObject(value, _jsonSerializerSettings);
        }

        public override object Execute(BaseRuntime runtime, Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults)
        {
            if (testResults.Count < 2)
                throw new InvalidOperationException("There must be at least 2 automatically executed test cases.");

            var inputEntries = testResults
                .Select(x => ConvertToSequenceDetectionInputEntry(x.Key, x.Value))
                .ToList();

            var sequenceDetectionFlowBlock = GetSequenceDetectionFlowBlock();
            SequenceDetectionService sequenceDetectionService = SequenceDetectionService.Instance;
            var sequenceDetectionPattern = sequenceDetectionService.Detect(new SequenceDetectionInputData()
            {
                Timeout = sequenceDetectionFlowBlock.MaxSequenceGenerationRuntimeSeconds,
                Entries = inputEntries
            });
            return sequenceDetectionPattern;
        }

        public class ExtractionResult
        {
            public FlowBlockTestDataset Entry { get; set; }
            public string ExpectedValue { get; set; }
            public int NumberOfDatasets { get; set; }
            public List<string> Messages { get; set; }

            public ExtractionResult()
            {
                Messages = new List<string>();
            }
        }

        private ExtractionResult ExtractAndValidate(FlowBloxTestDefinition testDefinition, BaseFlowBlock flowBlock)
        {
            var result = new ExtractionResult();

            result.Entry = testDefinition.Entries.SingleOrDefault(x => x.FlowBlock == flowBlock);
            if (result.Entry == null)
            {
                result.Messages.Add($"Could not find current flow block \"{flowBlock.Name}\" in test case \"{testDefinition.Name}\".");
                return result;
            }

            var testConfiguration = result.Entry.FlowBloxTestConfigurations.SingleOrDefault();
            if (testConfiguration == null)
            {
                result.Messages.Add($"There should be exactly one configuration in entry for flow block \"{flowBlock.Name}\" in test case \"{testDefinition.Name}\".");
                return result;
            }

            var numberOfDatasetsString = testConfiguration.ExpectationConditions?
                .FirstOrDefault(x => x.ExpectationConditionTarget == ExpectationConditionTarget.NumberOfDatasets)?.Value;

            if (string.IsNullOrEmpty(numberOfDatasetsString))
            {
                result.Messages.Add($"Could not find number of records in test case \"{testDefinition.Name}\".");
                return result;
            }

            if (!int.TryParse(numberOfDatasetsString, out var numberOfDatasets))
            {
                result.Messages.Add("The maximum number of records is not a number.");
                return result;
            }

            result.NumberOfDatasets = numberOfDatasets;

            result.ExpectedValue = testConfiguration.ExpectationConditions
                .FirstOrDefault(x => x.ExpectationConditionTarget == ExpectationConditionTarget.AnyValue ||
                                     x.ExpectationConditionTarget == ExpectationConditionTarget.FirstValue ||
                                     x.ExpectationConditionTarget == ExpectationConditionTarget.LastValue)?.Value;

            if (string.IsNullOrEmpty(result.ExpectedValue) && testConfiguration.SelectionMode == FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue)
            {
                result.ExpectedValue = testConfiguration.UserInput;
            }

            if (string.IsNullOrEmpty(result.ExpectedValue))
                result.Messages.Add($"Could not determine expected value from test case \"{testDefinition.Name}\".");

            return result;
        }

        public override bool CanExecute(out Dictionary<FlowBloxTestDefinition, List<string>> testDefinitionToMessages, out List<string> messages)
        {
            testDefinitionToMessages = new Dictionary<FlowBloxTestDefinition, List<string>>();

            var foundOKTestDefinitions = new List<FlowBloxTestDefinition>();

            foreach (var testDefinition in Source.TestDefinitions)
            {
                var sequenceDetectionFlowBlock = GetSequenceDetectionFlowBlock();
                var extractionResult = ExtractAndValidate(testDefinition, sequenceDetectionFlowBlock);

                if (extractionResult.Messages.Count > 0)
                {
                    testDefinitionToMessages[testDefinition] = extractionResult.Messages;
                }
                else
                {
                    foundOKTestDefinitions.Add(testDefinition);
                }
            }

            messages = new List<string>();
            if (foundOKTestDefinitions.Count < 2)
            {
                messages.Add("At least two test cases are required to execute sequence recognition.");
                return false;
            }

            return true;
        }

        private SequenceDetectionInputEntry ConvertToSequenceDetectionInputEntry(FlowBloxTestDefinition testDefinition, FlowBloxTestResult testResult)
        {
            var sequenceDetectionFlowBlock = GetSequenceDetectionFlowBlock();

            var validationResult = ExtractAndValidate(testDefinition, sequenceDetectionFlowBlock);

            if (validationResult.Messages.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, validationResult.Messages));

            if (InputField == null)
                throw new InvalidOperationException("Could not determine an input field.");

            if (!testResult.FieldValueAssignments.TryGetValue(InputField.FullyQualifiedName, out var content))
                throw new InvalidOperationException($"Could not determine content for the input field \"{InputField.FullyQualifiedName}\" from the underlying test result for test case \"{testDefinition.Name}\".");

            if (string.IsNullOrEmpty(content))
                throw new InvalidOperationException($"The content must not be null or empty for the input field from the underlying test result for test case \"{testDefinition.Name}\".");

            return new SequenceDetectionInputEntry(content, validationResult.ExpectedValue, validationResult.NumberOfDatasets);
        }
    }
}
