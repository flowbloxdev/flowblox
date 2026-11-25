using FlowBlox.Core.Util.Fields;

namespace FlowBloxTest.Components;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FieldListPresentationTests
{
    [TestMethod]
    public void Apply_OverwritesFollowingRowsAndPreservesUnreachedRows()
    {
        var entries = new List<string> { "before", "current", "replace", "keep" };

        FieldListPresentation.Apply(entries, 1, "first;second", ";");

        CollectionAssert.AreEqual(new[] { "before", "first", "second", "keep" }, entries);
    }

    [TestMethod]
    public void Apply_AppendsMissingRowsAndPreservesEmptyEntries()
    {
        var entries = new List<string> { "before", "current" };

        FieldListPresentation.Apply(entries, 1, "first;;third;", ";");

        CollectionAssert.AreEqual(new[] { "before", "first", "", "third", "" }, entries);
    }

    [TestMethod]
    [DataRow(@"\n")]
    [DataRow(@"\r\n")]
    [DataRow("\n")]
    [DataRow(null)]
    public void Apply_NormalizesMixedClipboardLineEndings(string separator)
    {
        var entries = new List<string> { "current" };

        FieldListPresentation.Apply(entries, 0, "first\r\nsecond\nthird\rfourth", separator);

        CollectionAssert.AreEqual(new[] { "first", "second", "third", "fourth" }, entries);
    }

    [TestMethod]
    public void Apply_ResolvesTabWithoutSplittingEmbeddedLineBreaks()
    {
        var entries = new List<string> { "current" };

        FieldListPresentation.Apply(entries, 0, "first\tsecond\nline", @"\t");

        CollectionAssert.AreEqual(new[] { "first", "second\nline" }, entries);
    }
}
