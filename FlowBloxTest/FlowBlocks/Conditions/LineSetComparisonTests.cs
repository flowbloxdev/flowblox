using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using Microsoft.Extensions.Caching.Memory;

namespace FlowBloxTest.FlowBlocks.Conditions;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class LineSetComparisonTests
{
    [TestMethod]
    [DataRow("alpha\nbeta\ngamma", "beta", true)]
    [DataRow("alpha\r\nbeta\rgamma", "gamma", true)]
    [DataRow("alpha\nbeta", "bet", false)]
    [DataRow("alpha\nbeta", "Beta", false)]
    [DataRow("alpha\n beta ", "beta", false)]
    [DataRow("alpha\n beta ", " beta ", true)]
    [DataRow("alpha\n\nbeta", "", true)]
    [DataRow("alpha\n", "", false)]
    [DataRow("", "", false)]
    [DataRow(null, "alpha", false)]
    [DataRow("alpha\nbeta", "alpha\nbeta", false)]
    public void Operators_CheckCompleteLines(string content, string value, bool expected)
    {
        var condition = new ComparisonCondition { Operator = ComparisonOperator.ContainsLine, Value = value };
        Assert.AreEqual(expected, condition.Compare(content));
        condition.Operator = ComparisonOperator.NotContainsLine;
        Assert.AreEqual(!expected, condition.Compare(content));
    }

    [TestMethod]
    public void Conditions_ObserveChangedFieldValuesAndResolveTheComparisonField()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var entry = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, fieldName: "Entry");
        entry.StringValue = "sourceid=one&hash=first";
        var content = new FieldElement(FieldTypes.Text) { StringValue = entry.StringValue + "\nother" };
        var condition = new FieldLogicalComparisonCondition
        {
            FieldElement = content,
            Operator = ComparisonOperator.ContainsLine,
            Value = entry.FullyQualifiedName
        };
        Assert.IsTrue(condition.Check());
        content.StringValue = "sourceid=one&hash=second\nother";
        Assert.IsFalse(condition.Check());
        entry.StringValue = "sourceid=one&hash=second";
        Assert.IsTrue(condition.Check());
    }

    [TestMethod]
    public void ConcurrentEqualStrings_ShareOneImmutableIndex()
    {
        using var cache = new LineSetCache(new MemoryCacheOptions());
        var sets = new FrozenSet<string>[32];
        Parallel.For(0, sets.Length, index =>
            sets[index] = cache.GetLines(new string("alpha\nbeta\ralpha".ToCharArray())));

        Assert.IsTrue(sets.All(set => ReferenceEquals(sets[0], set)));
        Assert.AreEqual(2, sets[0].Count);
    }

    [TestMethod]
    [DataRow(ComparisonOperator.ContainsLine)]
    [DataRow(ComparisonOperator.NotContainsLine)]
    public void LineOperators_RejectNonTextFieldsDuringValidation(ComparisonOperator op)
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var condition = new FieldComparisonCondition
        {
            FieldElement = new FieldElement(FieldTypes.Integer),
            Operator = op,
            Value = "1"
        };
        var context = new ValidationContext(condition) { MemberName = nameof(condition.FieldElement) };
        Assert.IsNotNull(FieldComparisonCondition.ValidateFieldElement(condition.FieldElement, context));
    }

    [TestMethod]
    public void ChangedContent_DoesNotKeepTheOldIndexAlive()
    {
        var clock = new LineSetCacheTestClock();
        using var cache = new LineSetCache(new MemoryCacheOptions { Clock = clock });
        var oldIndex = cache.GetLines("old\nvalue");
        clock.Advance(TimeSpan.FromSeconds(90));
        var currentIndex = cache.GetLines("new\nvalue");
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.AreSame(currentIndex, cache.GetLines("new\nvalue"));
        Assert.AreNotSame(oldIndex, cache.GetLines("old\nvalue"));
        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.AreSame(currentIndex, cache.GetLines(new string("new\nvalue".ToCharArray())));
        clock.Advance(TimeSpan.FromSeconds(121));
        Assert.AreNotSame(currentIndex, cache.GetLines("new\nvalue"));
    }
}
