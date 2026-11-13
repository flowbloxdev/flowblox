using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.CollectionOperations;
using FlowBlox.Core.Models.FlowBlocks.ControlFlow;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;

namespace FlowBloxTest.FlowBlocks.ControlFlow
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class RecursiveAndCollectionFlowBlockTests : FlowBloxTestsBase
    {
        private sealed class NumberSequenceFlowBlock : BaseSingleResultFlowBlock
        {
            public override FieldTypes DefaultResultFieldType => FieldTypes.Double;
            public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
            public override FlowBlockCategory GetCategory() => FlowBlockCategory.Calculation;
            public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
            {
                runtime.Focus(this);
                SetParentElement(data);
                GenerateResult(runtime, ["1", "2", "3"]);
            });
        }

        private sealed class MultiFieldSequenceFlowBlock : BaseResultFlowBlock
        {
            public List<FieldElement> ResultFields { get; } = [];

            public override List<FieldElement> Fields => ResultFields;
            public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
            public override FlowBlockCategory GetCategory() => FlowBlockCategory.Calculation;

            public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
            {
                GenerateResult(runtime,
                [
                    new Dictionary<FieldElement, string>
                    {
                        [ResultFields[0]] = "first-a",
                        [ResultFields[1]] = "first-b"
                    },
                    new Dictionary<FieldElement, string>
                    {
                        [ResultFields[0]] = "second-a",
                        [ResultFields[1]] = "second-b"
                    }
                ]);
            });
        }

        [TestInitialize]
        public void Initialize() => FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();

        [TestMethod]
        public void PossibleTargets_ContainsOnlyBlocksOnIncomingPath()
        {
            var start = CreateFlowBlock<StartFlowBlock>();
            var upstream = CreateFlowBlock<NumberSequenceFlowBlock>(start);
            var injection = CreateFlowBlock<InjectionFlowBlock>(upstream);
            var unrelated = CreateFlowBlock<NumberSequenceFlowBlock>();

            var possibleTargets = injection.GetPossibleTargetFlowBlocks();

            CollectionAssert.AreEquivalent(new BaseFlowBlock[] { start, upstream }, possibleTargets);
            CollectionAssert.DoesNotContain(possibleTargets, unrelated);
        }

        [TestMethod]
        public void CollectionQuery_Average_CollectsAllInputDatasets()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<NumberSequenceFlowBlock>(start);
            var query = CreateFlowBlock<CollectionQueryFlowBlock>(source);
            query.Operation = CollectionQueryOperation.Average;
            var output = Registry.CreateField(query, fieldType: FieldTypes.Double);
            query.OutputMappings.Add(new CollectionQueryFieldMapping { InputField = source.ResultField, OutputField = output });

            CreateRuntimeAndExecute(project);

            Assert.AreEqual("2", output.StringValue);
        }

        [TestMethod]
        public void CollectionQuery_First_StagesAndMapsCompleteDatasets()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var start = CreateFlowBlock<StartFlowBlock>();
            var source = CreateFlowBlock<MultiFieldSequenceFlowBlock>(start);
            var inputA = Registry.CreateField(source);
            var inputB = Registry.CreateField(source);
            source.ResultFields.AddRange([inputA, inputB]);

            var query = CreateFlowBlock<CollectionQueryFlowBlock>(source);
            query.Operation = CollectionQueryOperation.First;
            var outputA = Registry.CreateField(query);
            var outputB = Registry.CreateField(query);
            query.OutputMappings.Add(new CollectionQueryFieldMapping { InputField = inputA, OutputField = outputA });
            query.OutputMappings.Add(new CollectionQueryFieldMapping { InputField = inputB, OutputField = outputB });

            CreateRuntimeAndExecute(project);

            Assert.AreEqual("first-a", outputA.StringValue);
            Assert.AreEqual("first-b", outputB.StringValue);
        }

        [TestMethod]
        public void CollectionQuery_SupportsFieldsFromMultipleInputs()
        {
            var firstSource = CreateFlowBlock<NumberSequenceFlowBlock>();
            var secondSource = CreateFlowBlock<NumberSequenceFlowBlock>();
            var query = CreateFlowBlock<CollectionQueryFlowBlock>(firstSource);
            query.ReferencedFlowBlocks.Add(secondSource);

            var possibleFields = query.GetPossibleInputFields();

            Assert.AreEqual(FlowBlockCardinalities.Many, query.GetInputCardinality());
            CollectionAssert.AreEquivalent(
                new[] { firstSource.ResultField, secondSource.ResultField },
                possibleFields);
        }

        [TestMethod]
        public void CollectionQueryFieldMapping_RequiresInputAndOutputFields()
        {
            var mapping = new CollectionQueryFieldMapping();

            var errors = mapping.ValidateObject();

            Assert.IsTrue(errors.ContainsKey(nameof(CollectionQueryFieldMapping.InputField)));
            Assert.IsTrue(errors.ContainsKey(nameof(CollectionQueryFieldMapping.OutputField)));
        }

        [TestMethod]
        public void CollectionQuery_SynchronizeOutputFields_AddsAndUpdatesMappedFields()
        {
            var source = CreateFlowBlock<NumberSequenceFlowBlock>();
            var query = CreateFlowBlock<CollectionQueryFlowBlock>(source);

            query.SynchronizeOutputFields();
            var mapping = query.OutputMappings.Single();

            Assert.AreSame(source.ResultField, mapping.InputField);
            Assert.AreNotSame(source.ResultField, mapping.OutputField);
            Assert.AreEqual(source.ResultField.Name, mapping.OutputField.Name);
            Assert.AreEqual(FieldTypes.Double, mapping.OutputField.FieldType.FieldType);

            source.ResultField.Name = "Changed";
            source.ResultField.FieldType = new TypeElement { FieldType = FieldTypes.Integer };
            query.SynchronizeOutputFields();

            Assert.AreEqual("Changed", mapping.OutputField.Name);
            Assert.AreEqual(FieldTypes.Integer, mapping.OutputField.FieldType.FieldType);
        }
    }
}
