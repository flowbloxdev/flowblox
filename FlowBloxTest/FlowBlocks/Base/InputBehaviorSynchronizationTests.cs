using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.FlowBlocks.Base;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class InputBehaviorSynchronizationTests
{
    [TestMethod]
    public void Suppression_OnlySkipsInputBehaviorSynchronizationAndPreservesOtherEventsAndUpdates()
    {
        var project = new FlowBloxProject();
        using var registryScope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var context = Add(project, new StartFlowBlock { Name = "Start" });
        var first = Add(project, new FormatFlowBlock { Name = "First" });
        var second = Add(project, new FormatFlowBlock { Name = "Second" });
        var selector = Add(project, new StartEndPatternSelectorFlowBlock { Name = "Selector" });
        selector.ReferencedFlowBlocks.Add(first);
        selector.AssociatedIterationContext = context;
        selector.InputField = first.ResultField;
        var assignment = selector.InputBehaviorAssignments.Single();
        assignment.Behavior = InputBehavior.Last;
        var propertyNotified = false;
        var referencesNotified = false;
        var componentNotified = false;
        selector.PropertyChanged += (_, change) => propertyNotified |= change.PropertyName == nameof(selector.InputField);
        selector.ReferencedFlowBlocksChanged += _ => referencesNotified = true;
        selector.ComponentChanged += (_, _) => componentNotified = true;

        selector.SuppressInputBehaviorSynchronization();
        try
        {
            selector.InputField = second.ResultField;
            selector.ReferencedFlowBlocks.Add(second);
            Assert.AreSame(second.ResultField, selector.RequiredFields.Single());
            Assert.IsTrue(propertyNotified);
            Assert.IsTrue(referencesNotified);
            Assert.IsTrue(componentNotified);
            Assert.AreSame(assignment, selector.InputBehaviorAssignments.Single());
            Assert.AreEqual(InputBehavior.Last, assignment.Behavior);
        }
        finally
        {
            selector.RestoreInputBehaviorSynchronization();
        }

        Assert.AreSame(assignment, selector.InputBehaviorAssignments.Single(), "Restoring synchronization must not replay skipped changes.");
        selector.ReferencedFlowBlocks.Remove(second);
        selector.ReferencedFlowBlocks.Add(second);
        Assert.AreEqual(2, selector.InputBehaviorAssignments.Count);
        Assert.AreEqual(InputBehavior.Last, assignment.Behavior);
    }

    private static T Add<T>(FlowBloxProject project, T block) where T : BaseFlowBlock
    {
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        return block;
    }
}
