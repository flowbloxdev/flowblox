using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Models.Serialization;
using FlowBlox.Core.Models.FlowBlocks.ControlFlow;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.Web.InternalWebRequest;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Json;
using FlowBlox.Core.Models.FlowBlocks.Logic;

namespace FlowBlox.Core.Services
{
    public class FlowBloxCoreLegacyTypeMappingService : IFlowBloxLegacyTypeMappingService
    {
        public IEnumerable<FlowBloxLegacyTypeMapping> GetLegacyTypeMappings()
        {
            return
            [
                new FlowBloxLegacyTypeMapping(
                    typeof(FieldCopyFlowBlock),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Logic.DistributorFlowBlock, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(ComparisonCondition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.Condition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(FieldComparisonCondition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.FieldCondition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(LogicalGroupCondition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.SummarizationCondition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(InjectionFlowBlock),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.ControlFlow.RecursiveCallFlowBlock, FlowBlox.Core",
                        "FlowBlox.Core.Models.FlowBlocks.InvokerFlowBlock, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(InvocationFieldTransferConfig),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.InvocationFieldTransferConfig, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(FlowBloxFieldTestConfiguration),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.FlowBloxFieldTestConfiguration, FlowBlox.Core",
                        "FlowBlox.Core.Models.FlowBlocks.Additions.FlowBloxTestConfiguration, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(FlowBloxTestDefinition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.FlowBloxTestDefinition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(FlowBlockTestDataset),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.FlowBlockTestDataset, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(ExpectationCondition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.ExpectationCondition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(AIPropertyValueGenerationStrategy),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.AIRemote.AIPropertyValueGenerationStrategy, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(SequenceDetectionGenerationStrategy),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.SequenceDetection.SequenceDetectionGenerationStrategy, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(FlowBloxGenerationStrategyBase),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Additions.FlowBloxGenerationStrategyBase, FlowBlox.Core",
                        "FlowBlox.Core.Models.FlowBlocks.Additions.GenerationStrategyBase, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(WebRequestDestinations),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.WebRequest.WebRequestDestinations, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(ConfigurableWebRequest),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.WebRequest.ConfigurableWebRequest, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(ConfigurableWebRequestResult),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.WebRequest.ConfigurableWebRequestResult, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(ResponseBodyKind),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.WebRequest.ResponseBodyKind, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(WebRequestInvocationResult),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.WebRequest.WebRequestInvocationResult, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(WebRequestParameter),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.WebRequest.WebRequestParameter, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(StartEndPattern),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.StartEndPattern, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(TableColumnDefinition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.TableWriter.TableColumnDefinition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(TableSelectorMappingEntry),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.TableSelectorMappingEntry, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(TableSelectorColumnCondition),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.TableSelectorColumnCondition, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(JPathSelectorFlowBlock),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Json.JsonPathSelectorFlowBlock, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(JPathMultiSelectorFlowBlock),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Json.JsonManyPathsSelectorFlowBlock, FlowBlox.Core"
                    ]),
                new FlowBloxLegacyTypeMapping(
                    typeof(JPathMultiSelectorMappingEntry),
                    [
                        "FlowBlox.Core.Models.FlowBlocks.Json.JsonManyPathsSelectorMappingEntry, FlowBlox.Core"
                    ])
            ];
        }
    }
}
