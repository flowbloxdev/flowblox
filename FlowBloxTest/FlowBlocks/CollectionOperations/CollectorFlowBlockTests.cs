using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.CollectionOperations;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;

namespace FlowBloxTest.FlowBlocks.CollectionOperations
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class CollectorFlowBlockTests : FlowBloxTestsBase
    {
        private sealed class DatasetSourceFlowBlock : BaseResultFlowBlock
        {
            public List<FieldElement> ResultFields { get; } = [];
            public List<string[]> Rows { get; } = [];

            public override List<FieldElement> Fields => ResultFields;
            public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
            public override FlowBlockCategory GetCategory() => FlowBlockCategory.ControlFlow;

            public override bool Execute(BaseRuntime runtime, object data)
            {
                return Invoke(runtime, data, () =>
                {
                    var results = Rows.Select(row => ResultFields
                        .Select((field, index) => new { field, value = row[index] })
                        .ToDictionary(x => x.field, x => x.value));

                    GenerateResult(runtime, results);
                });
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        }

        [TestMethod]
        public void JoinCollector_CollectsValuesAndGeneratesSingleJoinedResult()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var source = CreateSource(start, ["alpha"], ["beta"], ["gamma"]);
            var collector = CreateFlowBlock<JoinCollectorFlowBlock>(source);
            collector.Separator = "|";
            collector.JoinedParameters.Add(source.ResultFields.Single());

            CreateRuntimeAndExecute(project);

            Assert.AreSame(start, collector.IterationContext);
            Assert.AreEqual("alpha|beta|gamma", collector.ResultField.StringValue);
            Assert.AreEqual(1, collector.GridElementResult.Results.Count);
        }

        [TestMethod]
        public void CollectionQuery_First_ReturnsFirstCompleteMappedDataset()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var source = CreateSource(
                start,
                ["job-1", "Pending"],
                ["job-2", "Success"]);
            var collector = CreateFlowBlock<CollectionQueryFlowBlock>(source);
            collector.Operation = CollectionQueryOperation.First;

            var outputId = AddMapping(collector, source.ResultFields[0]);
            var outputStatus = AddMapping(collector, source.ResultFields[1]);

            CreateRuntimeAndExecute(project);

            Assert.AreSame(start, collector.IterationContext);
            Assert.AreEqual("job-1", outputId.StringValue);
            Assert.AreEqual("Pending", outputStatus.StringValue);
            Assert.AreEqual(1, collector.GridElementResult.Results.Count);
        }

        [TestMethod]
        public void CollectionQuery_Any_ReturnsTrueWhenOneDatasetMatches()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var source = CreateSource(
                start,
                ["job-1", "Pending"],
                ["job-2", "Success"],
                ["job-3", "Failure"]);
            var collector = CreateFlowBlock<CollectionQueryFlowBlock>(source);
            collector.Operation = CollectionQueryOperation.Any;
            collector.PredicateResultField = Registry.CreateField(collector, fieldType: FieldTypes.Boolean);
            collector.Predicates.Add(new FieldLogicalComparisonCondition
            {
                FieldElement = source.ResultFields[1],
                Operator = ComparisonOperator.Equals,
                Value = "Success"
            });

            CreateRuntimeAndExecute(project);

            Assert.AreSame(start, collector.IterationContext);
            Assert.AreEqual(bool.TrueString.ToLowerInvariant(), collector.PredicateResultField.StringValue);
            Assert.AreEqual(1, collector.GridElementResult.Results.Count);
        }

        [TestMethod]
        public void JoinCollector_CollectsFormattedResultsProducedAtTheSharedOuterIterationEnd()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var configuration = CreateSource(start, ["config"]);
            var settings = CreateSource(configuration, ["ready"]);
            var messages = CreateSource(settings, ["alpha"], ["beta"], ["gamma"], ["delta"], ["epsilon"]);
            messages.AssociatedIterationContext = configuration;
            var format = CreateFlowBlock<FormatFlowBlock>(messages);
            format.FormatExpression = "Mail: {0}";
            format.FormatParameterDefinitions.Add(new FlowBlox.Core.Models.FlowBlocks.Format.FormatParameterDefinition
            {
                Field = messages.ResultFields.Single()
            });
            var collector = CreateFlowBlock<JoinCollectorFlowBlock>(format);
            collector.AssociatedIterationContext = configuration;
            collector.Separator = "|";
            collector.JoinedParameters.Add(format.ResultField);

            CreateRuntimeAndExecute(project);

            Assert.AreEqual("Mail: alpha|Mail: beta|Mail: gamma|Mail: delta|Mail: epsilon", collector.ResultField.StringValue);
            Assert.HasCount(5, collector.InputDatasets);
            Assert.HasCount(1, collector.GridElementResult.Results);
        }

        [TestMethod]
        public void JoinCollector_SharedIterationEndKeepsConsecutiveOuterGroupsSeparate()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var configuration = CreateSource(start, ["one"], ["two"]);
            var settings = CreateSource(configuration, ["ready"]);
            var messages = CreateSource(settings, ["alpha"], ["beta"]);
            messages.AssociatedIterationContext = configuration;
            var format = CreateFlowBlock<FormatFlowBlock>(messages);
            format.FormatExpression = "{0}: {1}";
            format.FormatParameterDefinitions.Add(new FlowBlox.Core.Models.FlowBlocks.Format.FormatParameterDefinition
            {
                Field = configuration.ResultFields.Single()
            });
            format.FormatParameterDefinitions.Add(new FlowBlox.Core.Models.FlowBlocks.Format.FormatParameterDefinition
            {
                Field = messages.ResultFields.Single()
            });
            var collector = CreateFlowBlock<JoinCollectorFlowBlock>(format);
            collector.AssociatedIterationContext = configuration;
            collector.Separator = "|";
            collector.JoinedParameters.Add(format.ResultField);
            var outerCollector = CreateFlowBlock<JoinCollectorFlowBlock>(collector);
            outerCollector.AssociatedIterationContext = start;
            outerCollector.Separator = ";";
            outerCollector.JoinedParameters.Add(collector.ResultField);

            CreateRuntimeAndExecute(project);

            Assert.AreEqual("one: alpha|one: beta;two: alpha|two: beta", outerCollector.ResultField.StringValue);
            Assert.HasCount(2, outerCollector.InputDatasets);
            Assert.HasCount(1, outerCollector.GridElementResult.Results);
        }

        private DatasetSourceFlowBlock CreateSource(
            BaseFlowBlock predecessor,
            params string[][] rows)
        {
            var source = CreateFlowBlock<DatasetSourceFlowBlock>(predecessor);
            var fieldCount = rows.First().Length;

            for (var index = 0; index < fieldCount; index++)
                source.ResultFields.Add(Registry.CreateField(source));

            source.Rows.AddRange(rows);
            return source;
        }

        private FieldElement AddMapping(
            CollectionQueryFlowBlock collector,
            FieldElement inputField)
        {
            var outputField = Registry.CreateField(collector, fieldType: inputField.FieldType.FieldType);
            collector.OutputMappings.Add(new CollectionQueryFieldMapping
            {
                InputField = inputField,
                OutputField = outputField
            });
            return outputField;
        }

    }
}
