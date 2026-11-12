using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.Runtime;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class UnresolvedFieldDebuggingTests
{
    [TestMethod]
    public void Invocation_ReportsLiveAssociatedValuesWithoutPausingOrBlocking()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Input");
        field.StringValue = "Ready";
        var executionCount = 0;
        var block = new UnresolvedFieldProbeFlowBlock
        {
            Name = "Probe",
            Text = field.FullyQualifiedName,
            Observe = () => executionCount++
        };
        registry.RegisterFlowBlock(block);
        var runtime = new TransientRuntime(project) { StopOnWarning = true };
        var warnings = CaptureWarnings(runtime);

        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.IsEmpty(warnings);

        field.StringValue = "$ABC::53";
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.HasCount(2, warnings);
        Assert.IsTrue(warnings.All(message => message.Contains("$ABC::53") && message.Contains(block.Name)));

        field.StringValue = "Resolved";
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.HasCount(2, warnings);
        Assert.AreEqual(4, executionCount);
        Assert.IsFalse(runtime.Pause);
        Assert.IsFalse(runtime.Aborted);
    }

    [TestMethod]
    public void Associations_AreCachedSeparatelyForEachRuntimeAndFlowBlock()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var firstField = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "First");
        firstField.StringValue = "$Missing::First";
        var secondField = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Second");
        secondField.StringValue = "$Missing::Second";
        var firstBlock = new UnresolvedFieldProbeFlowBlock { Name = "FirstProbe", Text = firstField.FullyQualifiedName };
        var secondBlock = new UnresolvedFieldProbeFlowBlock { Name = "SecondProbe", Text = secondField.FullyQualifiedName };
        registry.RegisterFlowBlock(firstBlock);
        registry.RegisterFlowBlock(secondBlock);
        var runtime = new TransientRuntime(project);
        var warnings = CaptureWarnings(runtime);

        Assert.IsTrue(firstBlock.Execute(runtime, null!));

        // Changing configuration here proves the collector is not repeated within a runtime.
        firstBlock.Text = secondField.FullyQualifiedName;
        Assert.IsTrue(firstBlock.Execute(runtime, null!));
        Assert.HasCount(2, warnings);
        Assert.IsTrue(warnings.All(message => message.Contains("$Missing::First")));

        Assert.IsTrue(secondBlock.Execute(runtime, null!));
        StringAssert.Contains(warnings.Last(), "$Missing::Second");

        var nextRuntime = new TransientRuntime(project);
        var nextWarnings = CaptureWarnings(nextRuntime);
        Assert.IsTrue(firstBlock.Execute(nextRuntime, null!));
        Assert.HasCount(1, nextWarnings);
        StringAssert.Contains(nextWarnings.Single(), "$Missing::Second");
    }

    [TestMethod]
    public void FailedRequirements_SkipReferenceWarningsAndAssociationCollection()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var required = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Required");
        required.StringValue = string.Empty;
        var firstField = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "First");
        firstField.StringValue = "$Missing::First";
        var secondField = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Second");
        secondField.StringValue = "$Missing::Second";
        var executed = false;
        var block = new UnresolvedFieldProbeFlowBlock
        {
            Name = "Probe",
            Text = firstField.FullyQualifiedName,
            Observe = () => executed = true
        };
        block.RequiredFields.Add(required);
        registry.RegisterFlowBlock(block);
        var runtime = new TransientRuntime(project);
        var warnings = CaptureWarnings(runtime);

        Assert.IsFalse(block.Execute(runtime, null!));
        Assert.IsFalse(executed);
        Assert.IsFalse(warnings.Any(message => message.Contains("Possible unresolved field reference")));

        required.StringValue = "Ready";
        block.Text = secondField.FullyQualifiedName;
        warnings.Clear();
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.IsTrue(executed);
        Assert.HasCount(1, warnings);
        StringAssert.Contains(warnings.Single(), "$Missing::Second");
    }

    private static List<string> CaptureWarnings(BaseRuntime runtime)
    {
        var warnings = new List<string>();
        runtime.LogMessageCreated += (_, message, level) =>
        {
            if (level == FlowBloxLogLevel.Warning)
                warnings.Add(message);
        };

        return warnings;
    }
}
