using FlowBlox.Core.Actions;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.Actions;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FlowBloxBaseActionTests
{
    [TestMethod]
    public void UndoRedo_NestedAssociatedChanges_RestoreOriginalStateInReverseOrder()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var target = new FormatFlowBlock { Name = "Target", FormatExpression = "original" };
        var firstGroup = new ActionGroup();
        firstGroup.AssociatedActions.Add(Change(target, "original", "first"));
        firstGroup.AssociatedActions.Add(Change(target, "first", "second"));
        var secondGroup = new ActionGroup();
        secondGroup.AssociatedActions.Add(Change(target, "second", "third"));
        var action = new ActionGroup();
        action.AssociatedActions.Add(firstGroup);
        action.AssociatedActions.Add(secondGroup);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            action.Invoke();
            Assert.AreEqual("third", target.FormatExpression);
            action.Undo();
            Assert.AreEqual("original", target.FormatExpression);
        }
    }

    private static FlowBloxPropertyChangeAction Change(FormatFlowBlock target, string before, string after)
        => new()
        {
            Target = target,
            PropertyName = nameof(FormatFlowBlock.FormatExpression),
            OldValue = before,
            NewValue = after
        };

    private sealed class ActionGroup : FlowBloxBaseAction
    {
    }
}
