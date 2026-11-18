using System.ComponentModel;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.Modifier;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Communication;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Enums;

namespace FlowBloxTest.FlowBlocks.Base;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class OutputSortingTests
{
    private FlowBloxProject _project;
    private IDisposable _scope;

    [TestInitialize]
    public void Initialize()
    {
        _project = new FlowBloxProject();
        _scope = FlowBloxRegistryProvider.BeginScopedRegistry(_project.FlowBloxRegistry);
        _project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
    }

    [TestCleanup]
    public void Cleanup() => _scope.Dispose();

    [TestMethod]
    [DataRow(FieldTypes.Integer, "10", "2", "2", "10")]
    [DataRow(FieldTypes.Long, "10000000000", "2", "2", "10000000000")]
    [DataRow(FieldTypes.Double, "10,5", "2,5", "2,5", "10,5")]
    [DataRow(FieldTypes.Float, "10,5", "2,5", "2,5", "10,5")]
    [DataRow(FieldTypes.DateTime, "01.12.2026", "02.01.2026", "02.01.2026", "01.12.2026")]
    [DataRow(FieldTypes.Boolean, "true", "false", "false", "true")]
    [DataRow(FieldTypes.ByteArray, "AQ==", "AA==", "AA==", "AQ==")]
    [DataRow(FieldTypes.Text, "beta", "alpha", "alpha", "beta")]
    public void Sorting_UsesConfiguredValuesWithoutChangingCurrentFields(
        FieldTypes type, string first, string second, string expectedFirst, string expectedSecond)
    {
        var block = CreateBlock(type);
        var field = block.Fields.Single();
        field.StringValue = second;
        block.OutputSorting.Single().Direction = OutputSortDirection.Ascending;

        block.Emit(new TransientRuntime(_project), [first], [second]);

        CollectionAssert.AreEqual(new[] { expectedFirst, expectedSecond }, Values(block));
        Assert.AreEqual(second, field.StringValue);
    }

    [TestMethod]
    public void Sorting_FollowsMovableKeyPriorityAndKeepsEqualKeysStable()
    {
        var block = CreateBlock(FieldTypes.Integer, FieldTypes.Text, FieldTypes.Text);
        block.OutputSorting[0].Direction = OutputSortDirection.Descending;
        block.OutputSorting[1].Direction = OutputSortDirection.Ascending;
        var rows = new[] { new[] { "2", "b", "first" }, new[] { "10", "b", "second" }, new[] { "2", "a", "third" }, new[] { "2", "a", "fourth" } };
        block.Emit(new TransientRuntime(_project), rows);
        CollectionAssert.AreEqual(new[] { "second", "third", "fourth", "first" }, Values(block, 2));

        block.OutputSorting.Move(1, 0);
        block.Emit(new TransientRuntime(_project), rows);
        CollectionAssert.AreEqual(new[] { "third", "fourth", "second", "first" }, Values(block, 2));
        block.OutputSorting[0].Direction = OutputSortDirection.Descending;
        block.Emit(new TransientRuntime(_project), rows);
        CollectionAssert.AreEqual(new[] { "second", "first", "third", "fourth" }, Values(block, 2));
    }

    [TestMethod]
    public void Sorting_DefaultRulesPreserveSuppliedOrderAndNullKeysSortBeforeValues()
    {
        var block = CreateBlock(FieldTypes.Integer);
        var rows = new[] { new[] { "10" }, new string[] { null }, new[] { "2" } };
        block.Emit(new TransientRuntime(_project), rows);
        CollectionAssert.AreEqual(new string[] { "10", null, "2" }, Values(block));

        block.OutputSorting.Single().Direction = OutputSortDirection.Ascending;
        block.Emit(new TransientRuntime(_project), rows);
        CollectionAssert.AreEqual(new string[] { null, "2", "10" }, Values(block));
    }

    [TestMethod]
    public void Synchronization_AddsDisabledRulesAndRemovesDeletedFieldsWithoutResettingExistingRules()
    {
        var block = CreateBlock(FieldTypes.Integer, FieldTypes.Text);
        var rules = block.OutputSorting;
        var retained = rules[1];
        retained.Direction = OutputSortDirection.Descending;
        rules.Move(1, 0);
        var extra = CreateField(block, FieldTypes.DateTime, "Extra");

        block.ResultFields.Add(extra);

        Assert.HasCount(3, rules);
        Assert.AreSame(retained, rules[0]);
        Assert.AreEqual(OutputSortDirection.None, rules.Single(rule => rule.Field == extra).Direction);
        block.ResultFields.Remove(extra);
        Assert.HasCount(2, rules);
        Assert.AreEqual(OutputSortDirection.Descending, retained.Direction);
    }

    [TestMethod]
    public void Synchronization_FollowsDeclarationReplacementAndTreatsDeletionAsInformational()
    {
        var block = new IMAPMailFetchFlowBlock { Name = "Mail" };
        block.OnAfterCreate();
        block.OnAfterLoad();
        var rules = block.OutputSorting;
        var declaration = block.ResultFields.First();
        var removedField = declaration.ResultField;
        declaration.ResultField = CreateField(block, FieldTypes.Text, "Replacement");
        var notify = typeof(FlowBlox.Core.Models.Base.FlowBloxReactiveObject)
            .GetMethod("OnPropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        notify.Invoke(declaration, [nameof(declaration.ResultField)]);

        Assert.IsFalse(rules.Any(rule => rule.Field == removedField));
        Assert.IsTrue(rules.Any(rule => rule.Field == declaration.ResultField));
        Assert.AreEqual(DeletionReviewStatus.Information, block.GetDeletionReviewStatus(
            rules[0], typeof(OutputSortRule).GetProperty(nameof(OutputSortRule.Field)), DeletionImpact.DirectReferenceRemoved,
            typeof(BaseResultFlowBlock).GetProperty(nameof(BaseResultFlowBlock.OutputSorting))));
    }

    [TestMethod]
    public void Sorting_UsesModifiedDatasetValues()
    {
        var block = CreateBlock(FieldTypes.Integer);
        block.Fields[0].Modifiers.Add(new TextLengthModifier());
        block.OutputSorting.Single().Direction = OutputSortDirection.Ascending;

        block.Emit(new TransientRuntime(_project), ["long text"], ["ab"]);

        CollectionAssert.AreEqual(new[] { "2", "9" }, Values(block));
    }

    [TestMethod]
    public void AssistantUpdate_UsesOrdinaryCollectionPathWithoutCreatingRows()
    {
        var block = CreateBlock(FieldTypes.Integer, FieldTypes.Text);
        var field = block.Fields[0];
        var result = FlowBlox.AIAssistant.Tools.Util.ToolHandlerUtilities.ApplyUpdates(
            new Newtonsoft.Json.Linq.JObject
            {
                ["path"] = "/OutputSorting/0/Direction",
                ["value"] = "Descending"
            }, block, _project.FlowBloxRegistry, "flowBlock", block.Name);

        Assert.IsTrue(result.Ok, result.Error);
        Assert.HasCount(2, block.OutputSorting);
        Assert.AreEqual(OutputSortDirection.Descending, block.OutputSorting.Single(rule => rule.Field == field).Direction);
    }

    private OutputSortProbeFlowBlock CreateBlock(params FieldTypes[] types)
    {
        var block = new OutputSortProbeFlowBlock { Name = "Sort" };
        for (var index = 0; index < types.Length; index++)
            block.ResultFields.Add(CreateField(block, types[index], "Field" + index));
        block.OnAfterLoad();
        return block;
    }

    private FieldElement CreateField(BaseResultFlowBlock source, FieldTypes type, string name) => new()
    {
        Name = name,
        Source = source,
        FieldType = new TypeElement
        {
            FieldType = type,
            DateFormat = "dd.MM.yyyy",
            DecimalSeparator = ",",
            GroupSeparator = "."
        }
    };

    private static string[] Values(BaseResultFlowBlock block, int index = 0) =>
        block.GridElementResult.Results.Select(row => row.FieldValueMappings[index].Value).ToArray();
}
