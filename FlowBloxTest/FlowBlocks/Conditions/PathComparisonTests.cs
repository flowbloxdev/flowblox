using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.FlowBlocks.Conditions;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class PathComparisonTests
{
    [TestMethod]
    [DataRow("{\"items\":[{\"id\":1},{\"id\":2}]}", "$.items[?(@.id == 2)]", true)]
    [DataRow("{\"items\":[{\"id\":1}]}", "$.items[?(@.id == 2)]", false)]
    [DataRow("{\"items\":[]}", "$.items[*]", false)]
    [DataRow("{\"items\":[]}", "$.items", true)]
    [DataRow("{\"value\":null}", "$.value", true)]
    [DataRow("{\"value\":false}", "$.value", true)]
    [DataRow("{\"value\":\"\"}", "$.value", true)]
    [DataRow("[1,2]", "$[*]", true)]
    [DataRow("{}", "$.missing", false)]
    public void JPath_ChecksExistenceOfSelectedTokens(string content, string path, bool hasResults)
    {
        Assert.AreEqual(hasResults, Compare(ComparisonOperator.JPathIsTrue, content, path));
        Assert.AreEqual(!hasResults, Compare(ComparisonOperator.JPathIsFalse, content, path));
    }

    [TestMethod]
    [DataRow("<root><item id='1'/><item id='2'/></root>", "//item[@id='2']", true)]
    [DataRow("<root><item id='1'/></root>", "//item[@id='2']", false)]
    [DataRow("<root><item id=''/></root>", "//item/@id", true)]
    [DataRow("<root><item/></root>", "//item", true)]
    [DataRow("<root><item>value</item></root>", "//item/text()", true)]
    [DataRow("<root/>", "//item", false)]
    public void XPath_ChecksExistenceOfSelectedNodes(string content, string path, bool hasResults)
    {
        Assert.AreEqual(hasResults, Compare(ComparisonOperator.XPathIsTrue, content, path));
        Assert.AreEqual(!hasResults, Compare(ComparisonOperator.XPathIsFalse, content, path));
    }

    [TestMethod]
    [DataRow("invalid", "$.item")]
    [DataRow("{", "$.item")]
    [DataRow("", "$.item")]
    [DataRow(null, "$.item")]
    [DataRow("{}", "$.items[")]
    [DataRow("{}", "")]
    [DataRow("{}", " ")]
    [DataRow("{}", null)]
    public void InvalidJsonOrJPath_ReturnsFalseForBothOperators(string content, string path)
    {
        Assert.IsFalse(Compare(ComparisonOperator.JPathIsTrue, content, path));
        Assert.IsFalse(Compare(ComparisonOperator.JPathIsFalse, content, path));
    }

    [TestMethod]
    [DataRow("invalid", "//item")]
    [DataRow("<root>", "//item")]
    [DataRow("", "//item")]
    [DataRow(null, "//item")]
    [DataRow("<root/>", "//item[")]
    [DataRow("<root/>", "")]
    [DataRow("<root/>", null)]
    [DataRow("<root/>", "count(//item)")]
    [DataRow("<!DOCTYPE root [<!ENTITY value 'test'>]><root>&value;</root>", "/root")]
    public void InvalidXmlOrXPath_ReturnsFalseForBothOperators(string content, string path)
    {
        Assert.IsFalse(Compare(ComparisonOperator.XPathIsTrue, content, path));
        Assert.IsFalse(Compare(ComparisonOperator.XPathIsFalse, content, path));
    }

    [TestMethod]
    public void PathConditions_ResolveRuntimeFields_AndWorkInLogicalConditions()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var path = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, fieldName: "Path");
        var content = new FieldElement(FieldTypes.Text) { StringValue = "{\"item\":null}" };
        path.StringValue = "$.item";
        Assert.IsTrue(new FieldComparisonCondition
        {
            FieldElement = content, Operator = ComparisonOperator.JPathIsTrue, Value = path.FullyQualifiedName
        }.Compare());
        Assert.IsTrue(new FieldLogicalComparisonCondition
        {
            FieldElement = content, Operator = ComparisonOperator.JPathIsTrue, Value = path.FullyQualifiedName
        }.Check());

        content.StringValue = "<root><item/></root>";
        path.StringValue = "//item";
        Assert.IsTrue(new FieldLogicalComparisonCondition
        {
            FieldElement = content, Operator = ComparisonOperator.XPathIsTrue, Value = path.FullyQualifiedName
        }.Check());
    }

    private static bool Compare(ComparisonOperator op, string content, string path) =>
        new ComparisonCondition { Operator = op, Value = path }.Compare(content);
}
