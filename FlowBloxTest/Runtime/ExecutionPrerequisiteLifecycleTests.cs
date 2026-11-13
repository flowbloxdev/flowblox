using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.Runtime;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExecutionPrerequisiteLifecycleTests
{
    [TestMethod]
    public void Invocation_ValidatesResolvedValuesBeforeExecutionWithoutChangingConfiguration()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        var count = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Integer, "Count");
        count.StringValue = "42";
        block.SetPropertySelectedField(nameof(block.Count), count);
        var observed = new List<string>();
        block.ObservePrerequisites = current =>
        {
            Assert.AreEqual(7, current.Count);
            observed.Add("validate:" + current.Resolve(owner => owner.Count));
        };
        block.ObserveExecution = () => observed.Add("execute:" + block.Resolve(owner => owner.Count));

        Assert.IsTrue(block.Execute(new TransientRuntime(project), null!));

        CollectionAssert.AreEqual(new[] { "validate:42", "execute:42" }, observed);
        Assert.AreEqual(7, block.Count);
    }

    [TestMethod]
    public void UnmetRequirements_DoNotValidateUnusedExecutionPrerequisites()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        block.RequirementsMet = false;
        block.ObservePrerequisites = _ => Assert.Fail("Skipped blocks must not require an installation.");
        block.ObserveExecution = () => Assert.Fail("Skipped blocks must not execute.");

        Assert.IsFalse(block.Execute(new TransientRuntime(project), null!));
    }

    [TestMethod]
    public void MissingInstallation_EmitsSetupErrorAndEmptyResultWithoutExecutingTheBlock()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        var failure = new ExecutionPrerequisiteException("probe-tool", typeof(ExecutionPrerequisiteProbeFlowBlock), "missing-tool");
        block.ObservePrerequisites = _ => throw failure;
        block.ObserveExecution = () => Assert.Fail("Missing execution prerequisites must skip the executor.");
        var errors = new List<string>();
        block.OnError += (_, message) => errors.Add(message);
        Exception? reportedFailure = null;
        var runtime = new TransientRuntime(project);
        runtime.LogEntryCreated += (_, _, _, exception) => reportedFailure ??= exception;

        Assert.IsTrue(block.Execute(runtime, null!));

        CollectionAssert.AreEqual(new[] { "probe-tool unavailable" }, errors);
        Assert.AreSame(failure, reportedFailure);
        Assert.IsNotNull(block.GridElementResult);
        Assert.IsTrue(block.GridElementResult.Results.SelectMany(result => result.FieldValueMappings)
            .All(mapping => string.IsNullOrEmpty(mapping.Value)));
    }

    [TestMethod]
    public void CancellationDuringPrerequisiteValidation_IsNotConvertedIntoASetupFailure()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        block.ObservePrerequisites = _ => throw new RuntimeCancellationException();
        block.ObserveExecution = () => Assert.Fail("Cancellation must skip execution.");
        block.OnError += (_, _) => Assert.Fail("Cancellation must not create an error notification.");

        Assert.Throws<RuntimeCancellationException>(() => block.Execute(new TransientRuntime(project), null!));
    }

    [TestMethod]
    public void BrowserWithEmptyUrl_PreservesItsWarningWithoutInitializingABrowser()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = new WebBrowserFlowBlock { Name = "Browser", Url = string.Empty };
        registry.RegisterFlowBlock(block);
        block.ResultField = registry.CreateField(block);
        string? warning = null;
        block.OnWarn += (_, message) => warning = message;
        block.OnError += (_, _) => Assert.Fail("An unused browser must not require an installation.");

        Assert.IsTrue(block.Execute(new TransientRuntime(project), null!));

        Assert.IsNotNull(warning);
        Assert.AreEqual(string.Empty, block.GridElementResult.Results.Single().FieldValueMappings.Single().Value);
    }

    [TestMethod]
    public void AssociatedManagedObjects_AreValidatedOnceBeforeExecutionEvenWithCyclesAndSharedReferences()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        var first = new ExecutionPrerequisiteProbeManagedObject { Name = "First" };
        var second = new ExecutionPrerequisiteProbeManagedObject { Name = "Second" };
        first.Child = second;
        second.Child = first;
        block.Dependency = first;
        block.OtherDependency = second;
        var order = new List<string>();
        block.ObservePrerequisites = _ => order.Add("block");
        first.ObservePrerequisites = () => order.Add("first");
        second.ObservePrerequisites = () => order.Add("second");
        block.ObserveExecution = () => order.Add("execute");

        Assert.IsTrue(block.Execute(new TransientRuntime(project), null!));

        CollectionAssert.AreEqual(new[] { "block", "first", "second", "execute" }, order);
        Assert.AreEqual(1, first.ValidationCount);
        Assert.AreEqual(1, second.ValidationCount);
    }

    [TestMethod]
    public void ChangedAssociation_IsEvaluatedOnTheNextInvocation()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        var first = new ExecutionPrerequisiteProbeManagedObject { Name = "First" };
        var second = new ExecutionPrerequisiteProbeManagedObject { Name = "Second" };
        var runtime = new TransientRuntime(project);
        block.Dependency = first;
        Assert.IsTrue(block.Execute(runtime, null!));
        block.Dependency = second;
        Assert.IsTrue(block.Execute(runtime, null!));

        Assert.AreEqual(1, first.ValidationCount);
        Assert.AreEqual(1, second.ValidationCount);
    }

    [TestMethod]
    public void AssociatedManagedObjectFailure_SkipsTheExecutorAndRetainsSetupDetails()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        var block = CreateProbe(project);
        var failure = new ExecutionPrerequisiteException("managed-tool", typeof(ExecutionPrerequisiteProbeManagedObject), "missing-tool");
        block.Dependency = new ExecutionPrerequisiteProbeManagedObject
        {
            Name = "Dependency",
            ObservePrerequisites = () => throw failure
        };
        var executed = false;
        block.ObserveExecution = () => executed = true;
        var errors = new List<string>();
        block.OnError += (_, message) => errors.Add(message);

        Assert.IsTrue(block.Execute(new TransientRuntime(project), null!));

        Assert.IsFalse(executed);
        CollectionAssert.AreEqual(new[] { "managed-tool unavailable" }, errors);
        Assert.IsNotNull(block.GridElementResult);
    }

    private static ExecutionPrerequisiteProbeFlowBlock CreateProbe(FlowBloxProject project)
    {
        var block = new ExecutionPrerequisiteProbeFlowBlock { Name = "Prerequisite probe" };
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        block.ResultField = project.FlowBloxRegistry.CreateField(block);
        return block;
    }
}
