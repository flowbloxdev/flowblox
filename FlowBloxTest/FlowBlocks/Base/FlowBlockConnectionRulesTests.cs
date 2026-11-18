using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Calculation;
using FlowBlox.Core.Models.FlowBlocks.Logic;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.FlowBlocks.Base;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FlowBlockConnectionRulesTests
{
    private FlowBloxProject _project;
    private IDisposable _scope;

    [TestInitialize]
    public void Initialize()
    {
        _project = new FlowBloxProject();
        _scope = FlowBloxRegistryProvider.BeginScopedRegistry(_project.FlowBloxRegistry);
    }

    [TestCleanup]
    public void Cleanup() => _scope.Dispose();

    [TestMethod]
    public void NextConnections_RejectCyclesDuplicatesAndFullInputs()
    {
        var start = Add<StartFlowBlock>("Start");
        var first = Add<MathFlowBlock>("First", start);
        var second = Add<MathFlowBlock>("Second", first);
        var source = Add<MathFlowBlock>("Source", second);
        var existing = Add<MathFlowBlock>("Existing", source);
        var fullInput = Add<FieldCopyFlowBlock>("Full", first);
        var available = Add<MathFlowBlock>("Available");
        var note = Add<NoteFlowBlock>("Note");

        var possible = source.GetPossibleNextFlowBlocks();

        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { available }, possible);
        Assert.IsFalse(possible.Contains(source));
        Assert.IsFalse(possible.Contains(start));
        Assert.IsFalse(possible.Contains(first));
        Assert.IsFalse(possible.Contains(second));
        Assert.IsFalse(possible.Contains(existing));
        Assert.IsFalse(possible.Contains(fullInput));
        Assert.IsFalse(possible.Contains(note));
    }

    [TestMethod]
    public void ReferencedChoices_UseNextConnectionRulesAndRetainCurrentAssociation()
    {
        var start = Add<StartFlowBlock>("Start");
        var source = Add<MathFlowBlock>("Source", start);
        var target = Add<FieldCopyFlowBlock>("Target", source);
        Add<MathFlowBlock>("Other");

        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { source }, target.GetPossibleReferencedElements());
        target.ReferencedFlowBlocks.Clear();
        foreach (var candidate in _project.FlowBloxRegistry.GetFlowBlocks())
            Assert.AreEqual(candidate.GetPossibleNextFlowBlocks().Contains(target), target.GetPossibleReferencedElements().Contains(candidate));
    }

    [TestMethod]
    public void IterationContexts_IncludeStartAndExcludeDirectPredecessorsDescendantsAndUnrelatedNodes()
    {
        var start = Add<StartFlowBlock>("Start");
        var first = Add<MathFlowBlock>("First", start);
        var direct = Add<MathFlowBlock>("Direct", first);
        var target = Add<MathFlowBlock>("Target", direct);
        Add<MathFlowBlock>("Successor", target);
        Add<MathFlowBlock>("Unrelated", start);

        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { start, first }, target.GetPossibleIterationContexts());
    }

    [TestMethod]
    public void IterationContexts_HideCompletedScopeInteriorButKeepBothBoundaries()
    {
        var start = Add<StartFlowBlock>("Start");
        var context = Add<MathFlowBlock>("Context", start);
        var firstInside = Add<MathFlowBlock>("FirstInside", context);
        var secondInside = Add<MathFlowBlock>("SecondInside", firstInside);
        var producer = Add<MathFlowBlock>("Producer", secondInside);
        producer.AssociatedIterationContext = context;
        var direct = Add<MathFlowBlock>("Direct", producer);
        var target = Add<MathFlowBlock>("Target", direct);

        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { start, context, producer }, target.GetPossibleIterationContexts());
    }

    [TestMethod]
    public void IterationContexts_DoNotExposeClosedScopeInteriorThroughAnotherBranch()
    {
        var start = Add<StartFlowBlock>("Start");
        var context = Add<MathFlowBlock>("Context", start);
        var inside = Add<MathFlowBlock>("Inside", context);
        var producer = Add<MathFlowBlock>("Producer", inside);
        producer.AssociatedIterationContext = context;
        var branch = Add<MathFlowBlock>("Branch", inside);
        var target = Add<MathFlowBlock>("Target", producer, branch);

        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { start, context }, target.GetPossibleIterationContexts());
    }

    [TestMethod]
    public void IterationContexts_RespectAutomaticallyResolvedScopes()
    {
        var start = Add<StartFlowBlock>("Start");
        var context = Add<MathFlowBlock>("Context", start);
        var left = Add<MathFlowBlock>("Left", context);
        var right = Add<MathFlowBlock>("Right", context);
        var merge = Add<MathFlowBlock>("Merge", left, right);
        var direct = Add<MathFlowBlock>("Direct", merge);
        var target = Add<MathFlowBlock>("Target", direct);

        Assert.AreSame(context, merge.IterationContext);
        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { start, context, merge }, target.GetPossibleIterationContexts());
    }

    [TestMethod]
    public void IterationContexts_ExcludeDisconnectedAncestors()
    {
        Add<StartFlowBlock>("Start");
        var isolated = Add<MathFlowBlock>("Isolated");
        var direct = Add<MathFlowBlock>("Direct", isolated);
        var target = Add<MathFlowBlock>("Target", direct);

        Assert.IsEmpty(target.GetPossibleIterationContexts());
    }

    [TestMethod]
    public void NormalConnections_AllowAcyclicForwardShortcuts()
    {
        var start = Add<StartFlowBlock>("Start");
        var first = Add<MathFlowBlock>("First", start);
        var second = Add<MathFlowBlock>("Second", first);
        var target = Add<MathFlowBlock>("Target", second);

        Assert.IsTrue(first.GetPossibleNextFlowBlocks().Contains(target));
        Assert.IsFalse(target.GetPossibleNextFlowBlocks().Contains(first));
    }

    [TestMethod]
    public void DropdownChoices_UseTheCurrentTransactionGraph()
    {
        var start = Add<StartFlowBlock>("Start");
        var predecessor = Add<MathFlowBlock>("Previous", start);
        var original = Add<MathFlowBlock>("Target", predecessor);
        var other = Add<MathFlowBlock>("Other");
        var copy = new MathFlowBlock { Name = original.Name };
        copy.ReferencedFlowBlocks.Add(predecessor);
        var transaction = new FlowBlox.Core.Provider.Registry.FlowBloxTransientRegistry(_project.FlowBloxRegistry);
        transaction.ReplaceRef(original, copy);
        using var transactionScope = FlowBloxRegistryProvider.BeginScopedRegistry(transaction);

        CollectionAssert.AreEquivalent(new BaseFlowBlock[] { start }, copy.GetPossibleIterationContexts());
        Assert.IsTrue(other.GetPossibleNextFlowBlocks().Contains(copy));
        Assert.IsTrue(copy.GetPossibleReferencedElements().Contains(other));
        Assert.IsFalse(copy.GetPossibleReferencedElements().Contains(copy));
    }

    private T Add<T>(string name, params BaseFlowBlock[] predecessors) where T : BaseFlowBlock, new()
    {
        var block = new T { Name = name };
        _project.FlowBloxRegistry.RegisterFlowBlock(block);
        foreach (var predecessor in predecessors)
            block.ReferencedFlowBlocks.Add(predecessor);
        return block;
    }
}
