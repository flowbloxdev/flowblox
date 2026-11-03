using System.Collections.ObjectModel;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Format;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Enums;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class DeletionReviewStatusTests
{
    [TestMethod]
    public void RemovingFormatParameter_UsesDefaultCollectionReviewWithComponentContext()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var source = CreateFormat(project, "Source");
        var consumer = CreateFormat(project, "Consumer");
        var parameter = new FormatParameterDefinition { Field = source.ResultField };
        consumer.FormatParameterDefinitions.Add(parameter);

        var dependency = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, parameter).Dependencies.Single();

        Assert.AreSame(consumer, dependency.EditTarget);
        Assert.AreEqual(nameof(FormatFlowBlock.FormatParameterDefinitions), dependency.ComponentProperty.Name);
        Assert.AreEqual(DeletionImpact.CollectionEntryRemoved, dependency.Impact);
        Assert.AreEqual(DeletionReviewStatus.Information, dependency.ReviewStatus);
        Assert.AreSame(parameter, consumer.FormatParameterDefinitions.Single());
    }

    [TestMethod]
    public void ClearingFormatParameterField_RequiresManualActionAndKeepsNestedPropertyContext()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var source = CreateFormat(project, "Source");
        var consumer = CreateFormat(project, "Consumer");
        var parameter = new FormatParameterDefinition { Field = source.ResultField };
        consumer.FormatParameterDefinitions.Add(parameter);

        var dependency = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source.ResultField).Dependencies
            .Single(item => ReferenceEquals(item.ReferencingObject, parameter));

        Assert.AreEqual(nameof(FormatParameterDefinition.Field), dependency.Property.Name);
        Assert.AreEqual(nameof(FormatFlowBlock.FormatParameterDefinitions), dependency.ComponentProperty.Name);
        Assert.AreEqual(DeletionReviewStatus.ManualActionRequired, dependency.ReviewStatus);
        Assert.AreSame(source.ResultField, parameter.Field);
    }

    [TestMethod]
    public void SharedReactiveObject_IsReviewedSeparatelyForEachComponentAndProperty()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var source = CreateFormat(project, "Source");
        var shared = new InputBehaviorAssignment { FlowBlock = source };
        var first = new ContextFlowBlock { Name = "First", CriticalAssignment = shared };
        first.InputBehaviorAssignments.Add(shared);
        var second = new ContextFlowBlock { Name = "Second", CriticalAssignment = shared };
        project.FlowBloxRegistry.RegisterFlowBlock(first);
        project.FlowBloxRegistry.RegisterFlowBlock(second);

        var dependencies = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source).Dependencies
            .Where(item => ReferenceEquals(item.ReferencingObject, shared)).ToList();

        Assert.AreEqual(3, dependencies.Count);
        Assert.AreEqual(DeletionReviewStatus.Information,
            dependencies.Single(item => item.ComponentProperty.Name == nameof(BaseFlowBlock.InputBehaviorAssignments)).ReviewStatus);
        Assert.IsTrue(dependencies.Where(item => item.ComponentProperty.Name == nameof(ContextFlowBlock.CriticalAssignment))
            .All(item => item.ReviewStatus == DeletionReviewStatus.ManualActionRequired));
        Assert.AreSame(source, shared.FlowBlock);
    }

    [TestMethod]
    public void ComponentOverride_CanRateNestedReferenceWithoutChangingReactiveObjectType()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var source = CreateFormat(project, "Source");
        var shared = new InputBehaviorAssignment { FlowBlock = source };
        var consumer = new ContextFlowBlock { Name = "Consumer", CriticalAssignment = shared };
        consumer.AutomaticallyCleanedAssignments.Add(shared);
        project.FlowBloxRegistry.RegisterFlowBlock(consumer);

        var dependencies = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source).Dependencies
            .Where(item => ReferenceEquals(item.ReferencingObject, shared)).ToList();

        Assert.AreEqual(DeletionReviewStatus.Information,
            dependencies.Single(item => item.ComponentProperty.Name == nameof(ContextFlowBlock.AutomaticallyCleanedAssignments)).ReviewStatus);
        Assert.AreEqual(DeletionReviewStatus.ManualActionRequired,
            dependencies.Single(item => item.ComponentProperty.Name == nameof(ContextFlowBlock.CriticalAssignment)).ReviewStatus);
    }

    [TestMethod]
    public void DeletingInputSource_ReportsInformationalAssignmentAndSupportsUndoRedo()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var source = CreateFormat(project, "Source");
        var consumer = CreateFormat(project, "Consumer");
        var iterationContext = new FlowBlox.Core.Models.FlowBlocks.SequenceFlow.StartFlowBlock { Name = "Start" };
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(iterationContext);
        project.FlowBloxRegistry.RegisterFlowBlock(iterationContext);
        source.ReferencedFlowBlocks.Add(iterationContext);
        consumer.ReferencedFlowBlocks.Add(source);
        consumer.AssociatedIterationContext = iterationContext;
        var assignment = consumer.InputBehaviorAssignments.Single();

        var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, source);
        Assert.AreEqual(DeletionReviewStatus.Information,
            report.Dependencies.Single(item => ReferenceEquals(item.ReferencingObject, assignment)).ReviewStatus);
        var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
        action.Invoke();
        Assert.AreEqual(0, consumer.InputBehaviorAssignments.Count);
        action.Undo();
        Assert.AreSame(source, consumer.InputBehaviorAssignments.Single().FlowBlock);
        action.Invoke();
        Assert.AreEqual(0, consumer.InputBehaviorAssignments.Count);
    }

    [TestMethod]
    public void DeletingSharedCollectionEntry_RemovesItOncePerOwnerAndPreservesOtherEntries()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var removed = new FormatParameterDefinition { IsRequired = true };
        var retained = new FormatParameterDefinition { IsRequired = false };
        var shared = new ParameterContainer();
        shared.Parameters.Add(removed);
        shared.Parameters.Add(retained);
        var separate = new ParameterContainer();
        separate.Parameters.Add(removed);
        separate.Parameters.Add(retained);
        var consumer = new CollectionContextBlock
        {
            Name = "Consumer",
            First = shared,
            Alias = shared,
            Second = separate
        };
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(consumer);
        project.FlowBloxRegistry.RegisterFlowBlock(consumer);

        var report = FlowBloxDeletionHelper.Analyze(project.FlowBloxRegistry, removed);
        Assert.AreEqual(3, report.Dependencies.Count);
        var action = FlowBloxDeletionHelper.CreateAction(project.FlowBloxRegistry, report);
        action.Invoke();

        Assert.IsFalse(consumer.First.Parameters.Single().IsRequired);
        Assert.IsFalse(consumer.Alias.Parameters.Single().IsRequired);
        Assert.IsFalse(consumer.Second.Parameters.Single().IsRequired);
        action.Undo();
        Assert.AreEqual(2, consumer.First.Parameters.Count);
        Assert.AreEqual(2, consumer.Second.Parameters.Count);
        action.Invoke();
        Assert.IsFalse(consumer.First.Parameters.Single().IsRequired);
        Assert.IsFalse(consumer.Second.Parameters.Single().IsRequired);
    }

    private sealed class ParameterContainer : FlowBlox.Core.Models.Base.FlowBloxReactiveObject
    {
        public ObservableCollection<FormatParameterDefinition> Parameters { get; set; } = new();
    }

    private sealed class CollectionContextBlock : FormatFlowBlock
    {
        public ParameterContainer First { get; set; }
        public ParameterContainer Alias { get; set; }
        public ParameterContainer Second { get; set; }
    }

    private static FormatFlowBlock CreateFormat(FlowBloxProject project, string name)
    {
        var block = new FormatFlowBlock { Name = name, FormatExpression = "value" };
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        return block;
    }

    private sealed class ContextFlowBlock : FormatFlowBlock
    {
        public InputBehaviorAssignment CriticalAssignment { get; set; }
        public ObservableCollection<InputBehaviorAssignment> AutomaticallyCleanedAssignments { get; set; } = new();

        public override DeletionReviewStatus GetDeletionReviewStatus(object owner, System.Reflection.PropertyInfo property,
            DeletionImpact impact, System.Reflection.PropertyInfo componentProperty)
            => componentProperty.Name == nameof(AutomaticallyCleanedAssignments)
                ? DeletionReviewStatus.Information
                : base.GetDeletionReviewStatus(owner, property, impact, componentProperty);
    }
}
