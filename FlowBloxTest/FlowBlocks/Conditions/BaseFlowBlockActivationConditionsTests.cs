using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBloxTest.FlowBlocks;
using FlowBloxTest.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.ObjectModel;
using System.Linq;

namespace FlowBloxTest.FlowBlocks.Conditions
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class BaseFlowBlockActivationConditionsTests : FlowBloxTestsBase
    {
        private FlowBloxProject _project;

        [TestInitialize]
        public void TestInitialize()
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            _project = project;
        }

        [TestMethod]
        [DataRow("$ABC::53")]
        [DataRow("Prefix $Unknown::Field suffix")]
        public void ValidateRequirements_PossibleUnresolvedReferences_DoNotBlock(string value)
        {
            var field = FieldMockCreator.CreateStringField("RequiredText", value);
            var block = CreateFlowBlock<StartFlowBlock>();
            block.RequiredFields.Add(field);

            Assert.IsTrue(block.ValidateRequirements(out var messages));
            Assert.IsEmpty(messages);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void ValidateRequirements_ReferenceResolvesToEmptyValue_Blocks(string value)
        {
            var source = FieldMockCreator.CreateStringField("EmptySource", value);
            var field = FieldMockCreator.CreateStringField("RequiredText", source.FullyQualifiedName);
            var block = CreateFlowBlock<StartFlowBlock>();
            block.RequiredFields.Add(field);

            Assert.IsFalse(block.ValidateRequirements(out var messages));
            Assert.IsTrue(messages.Any(message => message.Contains(field.FullyQualifiedName)));
        }

        [TestMethod]
        public void ValidateRequirements_NoActivationConditions_ReturnsTrue()
        {
            var registry = _project.FlowBloxRegistry;
            var block = CreateFlowBlock<StartFlowBlock>();

            block.ActivationConditions = new ObservableCollection<LogicalCondition>();

            var ok = block.ValidateRequirements(out var messages);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, messages.Count);
        }

        [TestMethod]
        public void ValidateRequirements_And_Fails_ReturnsFalse_AndAddsSummary()
        {
            var registry = _project.FlowBloxRegistry;
            var block = CreateFlowBlock<StartFlowBlock>();

            var c1 = CreateFieldLogicalConditionTrue("A", "X", LogicalOperator.And);
            var c2 = CreateFieldLogicalConditionFalse("B", "X", LogicalOperator.And);

            block.ActivationConditions = new ObservableCollection<LogicalCondition> { c1, c2 };

            var ok = block.ValidateRequirements(out var messages);

            Assert.IsFalse(ok);
            Assert.HasCount(1, messages);
        }

        [TestMethod]
        public void ValidateRequirements_Or_Succeeds_ReturnsTrue()
        {
            var registry = _project.FlowBloxRegistry;
            var block = CreateFlowBlock<StartFlowBlock>();

            var c1 = CreateFieldLogicalConditionFalse("A", "X", LogicalOperator.And);
            var c2 = CreateFieldLogicalConditionTrue("B", "X", LogicalOperator.Or);

            block.ActivationConditions = new ObservableCollection<LogicalCondition> { c1, c2 };

            var ok = block.ValidateRequirements(out var messages);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, messages.Count);
        }

        [TestMethod]
        public void ValidateRequirements_NestedGroup_Succeeds()
        {
            var registry = _project.FlowBloxRegistry;
            var block = CreateFlowBlock<StartFlowBlock>();

            // Outer: true AND (false OR true) => true
            var outer1 = CreateFieldLogicalConditionTrue("A", "X", LogicalOperator.And);

            var inner1 = CreateFieldLogicalConditionFalse("B", "X", LogicalOperator.And);
            var inner2 = CreateFieldLogicalConditionTrue("C", "X", LogicalOperator.Or);

            var innerGroup = new LogicalGroupCondition
            {
                LogicalOperator = LogicalOperator.And,
                Conditions = new ObservableCollection<LogicalCondition> { inner1, inner2 }
            };

            block.ActivationConditions = new ObservableCollection<LogicalCondition> { outer1, innerGroup };

            var ok = block.ValidateRequirements(out var messages);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, messages.Count);
        }

        private static FieldLogicalComparisonCondition CreateFieldLogicalConditionTrue(string fieldName, string expected, LogicalOperator op)
        {
            var field = FieldMockCreator.CreateStringField(fieldName, expected);

            return new FieldLogicalComparisonCondition
            {
                LogicalOperator = op,
                FieldElement = field,
                Operator = ComparisonOperator.Equals,
                Value = expected
            };
        }

        private static FieldLogicalComparisonCondition CreateFieldLogicalConditionFalse(string fieldName, string expected, LogicalOperator op)
        {
            var field = FieldMockCreator.CreateStringField(fieldName, "DIFFERENT");

            return new FieldLogicalComparisonCondition
            {
                LogicalOperator = op,
                FieldElement = field,
                Operator = ComparisonOperator.Equals,
                Value = expected
            };
        }
    }
}
