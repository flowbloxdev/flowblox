using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.Modifier;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.Runtime;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FieldResolverTests
{
    private FlowBloxProject _project = null!;
    private IDisposable _scope = null!;

    [TestInitialize]
    public void Initialize()
    {
        _project = new FlowBloxProject();
        _scope = FlowBloxRegistryProvider.BeginScopedRegistry(_project.FlowBloxRegistry);
        var start = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(start);
        _project.FlowBloxRegistry.RegisterFlowBlock(start);
    }

    [TestCleanup]
    public void Cleanup() => _scope.Dispose();

    [TestMethod]
    public void Resolve_TracksStringFieldsAndScalarSelectionsWithoutChangingConfiguration()
    {
        var text = Field("Text", "first");
        var count = Field("Count", "3");
        var modifier = new SubstringModifier { Index = 1, Length = 1 };
        modifier.SetPropertySelectedField(nameof(modifier.Index), count);
        var block = new ResolutionProbeFlowBlock { Text = "Value: " + text.FullyQualifiedName };

        Assert.AreEqual("Value: first", block.Resolve(x => x.Text));
        Assert.AreEqual(3, modifier.Resolve(x => x.Index));
        text.StringValue = "second";
        count.StringValue = "5";
        Assert.AreEqual("Value: second", block.Resolve(x => x.Text));
        Assert.AreEqual(5, modifier.Resolve(x => x.Index));
        Assert.AreEqual(1, modifier.Index);
        Assert.AreEqual("Value: " + text.FullyQualifiedName, block.Text);
    }

    [TestMethod]
    public void Resolve_RequiredAndTryDistinguishMissingValuesFromZeroAndFalse()
    {
        var block = new ResolutionProbeFlowBlock();
        Assert.AreEqual(string.Empty, block.Resolve(x => x.Text));
        Assert.IsFalse(block.TryResolve(x => x.Text, out var text));
        Assert.AreEqual(string.Empty, text);
        Assert.ThrowsExactly<ValidationException>(() => block.Resolve(x => x.Text, required: true));
        block.Text = " \t";
        Assert.IsFalse(block.TryResolve(x => x.Text, out _));

        var modifier = new SubstringModifier { Index = 0, Length = 1 };
        Assert.IsTrue(modifier.TryResolve(x => x.Index, out var index));
        Assert.AreEqual(0, index);
        var scalar = new ScalarFieldProbeFlowBlock();
        Assert.IsFalse(scalar.TryResolve(x => x.Since, out _));
        Assert.ThrowsExactly<ValidationException>(() => scalar.Resolve(x => x.Since, required: true));
        Assert.IsFalse(scalar.Resolve(x => x.Enabled, required: true));
    }

    [TestMethod]
    public void ResolveOrCreateNotification_NonResultBlockCompletesOnceAndCallerReturns()
    {
        var block = new ResolutionProbeFlowBlock { Name = "Probe" };
        var runtime = new TransientRuntime(_project);
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.AreEqual(1, block.Continuations);
        Assert.IsFalse(block.ConsumedValue);
        block.Text = "ready";
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.AreEqual(2, block.Continuations);
        Assert.IsTrue(block.ConsumedValue);
    }

    [TestMethod]
    [DataRow(false, 1, null)]
    [DataRow(true, 0, "")]
    public void Format_EmptyFieldResolutionRespectsAllowEmptySetting(bool allowEmpty, int expectedWarnings, string? expectedValue)
    {
        var empty = Field("Empty", string.Empty);
        var block = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<FormatFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        _project.FlowBloxRegistry.RegisterFlowBlock(block);
        block.AllowEmptyFormatExpression = allowEmpty;
        var runtime = new TransientRuntime(_project);
        var warnings = 0;
        block.OnWarn += (_, _) => warnings++;

        block.FormatExpression = empty.FullyQualifiedName;
        Assert.IsTrue(block.Execute(runtime, null!));
        Assert.AreEqual(expectedWarnings, warnings);
        Assert.AreEqual(empty.FullyQualifiedName, block.FormatExpression);

        Assert.AreEqual(expectedValue, block.GridElementResult.Results.Single()
            .FieldValueMappings.Single().Value);
    }

    private FieldElement Field(string name, string value)
    {
        var field = _project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, name);
        field.StringValue = value;
        return field;
    }
}
