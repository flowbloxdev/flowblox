using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Validations;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;

namespace FlowBloxTest.Util
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class FlowBloxComponentValidatorTests
    {
        [TestMethod]
        public void Validate_IgnoresUnboundInputAssignmentOnlyInAutomaticInputCollection()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            var unbound = new InputBehaviorAssignment();
            var block = new AssignmentContextBlock { Name = "Consumer", OtherAssignment = unbound, FormatExpression = "value" };
            block.InputBehaviorAssignments.Add(unbound);

            var messages = FlowBloxComponentValidator.Validate(block);

            Assert.IsFalse(messages.Any(message => message.StartsWith("[InputBehaviorAssignments[", StringComparison.Ordinal)));
            Assert.IsTrue(messages.Any(message => message.StartsWith("[OtherAssignment]", StringComparison.Ordinal)));
            Assert.AreSame(unbound, block.InputBehaviorAssignments.Single(), "Validation must not remove persisted entries.");
        }

        private sealed class AssignmentContextBlock : FormatFlowBlock
        {
            public InputBehaviorAssignment OtherAssignment { get; set; }
        }

        [TestMethod]
        public void Validate_ValidatesManagedObjectWhenItIsTheRoot()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();

            var messages = FlowBloxComponentValidator.Validate(new SQLTable());

            Assert.IsTrue(messages.Count > 0);
        }

        [TestMethod]
        public void Validate_ValidatesOwnedReactiveObjectsWithoutFollowingComponentReferences()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            var reader = new TableReaderFlowBlock
            {
                Name = "Reader",
                ReferencedTable = new SQLTable()
            };
            reader.MappingEntries.Add(new TableSelectorMappingEntry
            {
                ColumnName = "Id"
            });
            reader.ReferencedFlowBlocks.Add(new StartFlowBlock());

            var messages = FlowBloxComponentValidator.Validate(reader);

            Assert.IsTrue(messages.Any(x => x.StartsWith("[MappingEntries[0]]", StringComparison.Ordinal)));
            Assert.IsFalse(messages.Any(x => x.StartsWith("[ReferencedTable]", StringComparison.Ordinal)));
            Assert.IsFalse(messages.Any(x => x.StartsWith("[ReferencedFlowBlocks", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void Validate_DoesNotResolveOnDemandByteArrayValue()
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var field = new FieldElement(FieldTypes.Text)
            {
                Name = "Response",
                StringValue = "{\"jobs\":[]}"
            };
            project.FlowBloxRegistry.Register(field);

            var messages = FlowBloxComponentValidator.Validate(field.FieldType);

            Assert.AreEqual(0, messages.Count);
        }
    }
}
