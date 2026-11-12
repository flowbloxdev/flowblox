using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.Runtime;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ScalarFieldSelectionRuntimeTests
{
    [TestMethod]
    public void UnenabledFieldSelection_IgnoresStoredAssignmentsDuringCollectionAndInvocation()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var block = new ScalarFieldProbeFlowBlock { Name = "Probe" };
        registry.RegisterFlowBlock(block);
        var field = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Integer, "Disabled");
        field.StringValue = "42";
        block.Settings.SetPropertySelectedField(nameof(block.Settings.LiteralOnly), field);
        var control = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Boolean, "Control");
        control.StringValue = "true";
        block.SetPropertySelectedField(nameof(block.InputIgnoreDuplicates), control);
        block.Observe = current =>
        {
            Assert.AreEqual(15, current.Settings.LiteralOnly);
            Assert.IsFalse(current.InputIgnoreDuplicates);
        };

        Assert.IsFalse(block.GetAssociatedFields().Contains(field));
        Assert.IsFalse(block.GetAssociatedFields().Contains(control));
        Assert.IsTrue(block.Execute(new TransientRuntime(project), null!));
        Assert.AreEqual(15, FlowBlox.Core.Util.Fields.FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(
            block.Settings, settings => settings.LiteralOnly));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Invocation_ResolvesScalarsAndNestedConfigurationThenRestoresLiterals(bool throwInExecutor)
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var block = new ScalarFieldProbeFlowBlock { Name = "Probe" };
        registry.RegisterFlowBlock(block);
        var count = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Count");
        count.StringValue = "42";
        var enabled = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Enabled");
        enabled.StringValue = "true";
        var date = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Since");
        date.StringValue = "2026-11-05";
        var encoding = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Encoding");
        encoding.StringValue = "UTF8";
        block.SetPropertySelectedField(nameof(block.Count), count);
        block.SetPropertySelectedField(nameof(block.Enabled), enabled);
        block.SetPropertySelectedField(nameof(block.Since), date);
        block.SetPropertySelectedField(nameof(block.Encoding), encoding);
        block.Settings.SetPropertySelectedField(nameof(block.Settings.Limit), count);
        var observed = false;
        block.Observe = current =>
        {
            Assert.AreEqual(42, current.Count);
            Assert.IsTrue(current.Enabled);
            Assert.AreEqual(new DateTime(2026, 11, 5), current.Since);
            Assert.AreEqual(DotNetEncodingNames.UTF8, current.Encoding);
            Assert.AreEqual(42, current.Settings.Limit);
            observed = true;
            if (throwInExecutor)
                throw new InvalidOperationException("Expected probe failure.");
        };

        var runtime = new TransientRuntime(project);
        Assert.AreEqual(!throwInExecutor, block.Execute(runtime, null!));
        Assert.IsTrue(observed);
        Assert.AreEqual(7, block.Count);
        Assert.IsFalse(block.Enabled);
        Assert.IsNull(block.Since);
        Assert.AreEqual(DotNetEncodingNames.ASCII, block.Encoding);
        Assert.AreEqual(10, block.Settings.Limit);

        if (!throwInExecutor)
        {
            count.StringValue = "51";
            block.Observe = current => Assert.AreEqual(51, current.Count);
            Assert.IsTrue(block.Execute(runtime, null!));
            Assert.AreEqual(7, block.Count);
        }
    }

    [TestMethod]
    public void FailedConversion_RestoresPreviouslyAppliedProperties()
    {
        var project = new FlowBloxProject();
        var registry = project.FlowBloxRegistry;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(registry);
        var block = new ScalarFieldProbeFlowBlock { Name = "Probe" };
        registry.RegisterFlowBlock(block);
        var valid = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Valid");
        valid.StringValue = "42";
        var invalid = registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Invalid");
        invalid.StringValue = "not-a-boolean";
        block.SetPropertySelectedField(nameof(block.Count), valid);
        block.SetPropertySelectedField(nameof(block.Enabled), invalid);

        Assert.IsFalse(block.Execute(new TransientRuntime(project), null!));
        Assert.AreEqual(7, block.Count);
        Assert.IsFalse(block.Enabled);
    }
}
