using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Logic;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Test.Runtime;

namespace FlowBloxTest.FlowBlocks.Logic
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class FieldCopyScopeTests : FlowBloxTestsBase
    {
        [TestMethod]
        public void SelectionScopes_DistinguishDirectPredecessorsAvailableFieldsAndUserFieldTypes()
        {
            FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
            var start = CreateFlowBlock<StartFlowBlock>();
            var ancestor = CreateFlowBlock<FieldCopyFlowBlock>(start);
            var ancestorField = Registry.CreateField(ancestor);
            ancestor.CopiedFields.Add(ancestorField);
            var predecessor = CreateFlowBlock<FieldCopyFlowBlock>(ancestor);
            var predecessorField = Registry.CreateField(predecessor);
            predecessor.CopiedFields.Add(predecessorField);
            var copy = CreateFlowBlock<FieldCopyFlowBlock>(predecessor);
            var ownField = Registry.CreateField(copy);
            copy.CopiedFields.Add(ownField);
            var input = Registry.CreateUserField(UserFieldTypes.Input);
            var memory = Registry.CreateUserField(UserFieldTypes.Memory);

            CollectionAssert.AreEquivalent(new[] { predecessorField }, copy.GetPossibleInputFields());

            copy.InputFieldScope = FieldCopyInputScope.AllFields;
            CollectionAssert.AreEquivalent(
                new[] { ancestorField, predecessorField, input, memory },
                copy.GetPossibleInputFields());

            copy.InputFieldScope = FieldCopyInputScope.InputFields;
            CollectionAssert.AreEquivalent(new[] { input }, copy.GetPossibleInputFields());

            copy.InputFieldScope = FieldCopyInputScope.MemoryFields;
            CollectionAssert.AreEquivalent(new[] { memory }, copy.GetPossibleInputFields());
        }

        [TestMethod]
        [DataRow(UserFieldTypes.Input, FieldCopyInputScope.InputFields)]
        [DataRow(UserFieldTypes.Memory, FieldCopyInputScope.MemoryFields)]
        public void Execute_MaterializesUserFieldAsIndependentOutputSelectableBySplit(
            UserFieldTypes userFieldType,
            FieldCopyInputScope scope)
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var start = CreateFlowBlock<StartFlowBlock>();
            var copy = CreateFlowBlock<FieldCopyFlowBlock>(start);
            var source = Registry.CreateUserField(userFieldType);
            source.StringValue = "first,second";
            copy.InputFieldScope = scope;
            copy.InputField = source;
            var output = Registry.CreateField(copy);
            copy.CopiedFields.Add(output);

            copy.Execute(new FlowBloxUnitTestRuntime(project), null);

            var result = copy.GridElementResult.Results.Single().FieldValueMappings.Single();
            Assert.AreSame(output, result.Field);
            Assert.AreEqual("first,second", result.Value);
            Assert.IsFalse(output.UserField);
            Assert.AreEqual("first,second", source.StringValue);

            var split = new SplitFlowBlock();
            split.ReferencedFlowBlocks.Add(copy);
            CollectionAssert.Contains(split.GetPossibleInputFields(), output);
        }
    }
}
