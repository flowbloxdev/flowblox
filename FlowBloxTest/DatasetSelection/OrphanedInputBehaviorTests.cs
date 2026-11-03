using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Base.DatasetSelection;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;

namespace FlowBloxTest.DatasetSelection;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class OrphanedInputBehaviorTests
{
    [TestMethod]
    public void Factory_IgnoresNullAndUnboundRowWiseAssignmentsAlongsideValidCrossInput()
    {
        var source = new FormatFlowBlock { Name = "Source" };
        var field = new FieldElement { Name = "Value", Source = source };
        var output = new FlowBlockOut
        {
            Results = new List<FlowBlockOutDataset>
            {
                new() { FieldValueMappings = new List<FlowBlockOutDatasetFieldValueMapping>
                    { new() { Field = field, Value = "expected", PrecedingFieldValues = new() } } }
            }
        };
        var passed = new Dictionary<BaseFlowBlock, HashSet<FlowBlockOut>> { [source] = new() { output } };
        var assignments = new List<InputBehaviorAssignment>
        {
            null,
            new() { Behavior = InputBehavior.RowWise },
            new() { FlowBlock = source, Behavior = InputBehavior.Cross }
        };

        var selector = FlowBlockDatasetSelectorFactory.Create(passed, assignments);
        Assert.IsInstanceOfType<FlowBlockCombinationDatasetSelector>(selector);
        Assert.AreEqual("expected", selector.GetResults().Single().FieldValueMappings.Single().Value);
        Assert.AreEqual("expected", new FlowBlockCombinationDatasetSelector(passed, assignments)
            .GetResults().Single().FieldValueMappings.Single().Value);
        Assert.AreEqual(3, assignments.Count, "Dataset selection must not mutate persisted configuration.");
    }

    [TestMethod]
    public void Factory_IgnoresUnboundCrossAssignmentAlongsideValidRowWiseInput()
    {
        var source = new FormatFlowBlock { Name = "Source" };
        var field = new FieldElement { Name = "Value", Source = source };
        var passed = new Dictionary<BaseFlowBlock, HashSet<FlowBlockOut>>
        {
            [source] = new()
            {
                new() { Results = new List<FlowBlockOutDataset>
                {
                    new() { FieldValueMappings = new List<FlowBlockOutDatasetFieldValueMapping>
                        { new() { Field = field, Value = "row", PrecedingFieldValues = new() } } }
                } }
            }
        };
        var assignments = new List<InputBehaviorAssignment>
        {
            null,
            new() { Behavior = InputBehavior.Cross },
            new() { FlowBlock = source, Behavior = InputBehavior.RowWise }
        };

        var selector = FlowBlockDatasetSelectorFactory.Create(passed, assignments);
        Assert.IsInstanceOfType<FlowBlockRowWiseDatasetSelector>(selector);
        Assert.AreEqual("row", selector.GetResults().Single().FieldValueMappings.Single().Value);
        Assert.AreEqual("row", new FlowBlockRowWiseDatasetSelector(passed, assignments)
            .GetResults().Single().FieldValueMappings.Single().Value);
    }
}
