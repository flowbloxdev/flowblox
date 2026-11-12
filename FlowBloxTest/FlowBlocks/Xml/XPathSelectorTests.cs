using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.Xml;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Test.Runtime;
using System.Xml;

namespace FlowBloxTest.FlowBlocks.Xml
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class XPathSelectorTests : FlowBloxTestsBase
    {
        private FlowBloxProject _project;

        [TestInitialize]
        public void TestInitialize()
        {
            _project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = _project;
        }

        [TestMethod]
        public void SelectValues_ReturnsAttributeValuesForAttributeXPath()
        {
            var document = new XmlDocument();
            document.LoadXml("<root><item id=\"one\"/><item id=\"two\"/></root>");

            var values = XPathSelector.SelectValues(document, "//item/@id");

            CollectionAssert.AreEqual(new[] { "one", "two" }, values);
        }

        [TestMethod]
        public void SelectValues_UsesInnerXmlByDefault()
        {
            var document = new XmlDocument();
            document.LoadXml("<root><Country><Code>DE</Code></Country></root>");

            var values = XPathSelector.SelectValues(document, "//Country");

            CollectionAssert.AreEqual(new[] { "<Code>DE</Code>" }, values);
        }

        [TestMethod]
        public void SelectValues_ReturnsOuterXmlForElements()
        {
            var document = new XmlDocument();
            document.LoadXml("<root><Country><Code>DE</Code></Country></root>");

            var values = XPathSelector.SelectValues(
                document,
                "//Country",
                XPathResultMode.OuterXml);

            CollectionAssert.AreEqual(new[] { "<Country><Code>DE</Code></Country>" }, values);
        }

        [TestMethod]
        public void SelectValues_ReturnsOuterXmlForAttributes()
        {
            var document = new XmlDocument();
            document.LoadXml("<root><item id=\"one\"/></root>");

            var values = XPathSelector.SelectValues(
                document,
                "//item/@id",
                XPathResultMode.OuterXml);

            CollectionAssert.AreEqual(new[] { "id=\"one\"" }, values);
        }

        [TestMethod]
        public void XPathSelectorFlowBlock_UsesXmlContentAndReturnsAllMatches()
        {
            var startFlowBlock = CreateFlowBlock<StartFlowBlock>();
            var selector = CreateFlowBlock<XPathSelectorFlowBlock>(startFlowBlock);
            selector.XmlContent = "<root><item>one</item><item>two</item></root>";
            selector.XPath = "//item";

            var runtime = new FlowBloxUnitTestRuntime(_project);

            Assert.IsTrue(selector.Execute(runtime, null));
            CollectionAssert.AreEqual(new[] { "one", "two" }, GetResultValues(selector));
            Assert.AreEqual(FlowBlockCardinalities.Many, selector.GetInputCardinality());
        }

        [TestMethod]
        public void XPathSelectorFlowBlock_ReturnsAttributeValues()
        {
            var startFlowBlock = CreateFlowBlock<StartFlowBlock>();
            var selector = CreateFlowBlock<XPathSelectorFlowBlock>(startFlowBlock);
            selector.XmlContent = "<root><item id=\"one\"/><item id=\"two\"/></root>";
            selector.XPath = "//item/@id";

            var runtime = new FlowBloxUnitTestRuntime(_project);

            Assert.IsTrue(selector.Execute(runtime, null));
            CollectionAssert.AreEqual(new[] { "one", "two" }, GetResultValues(selector));
        }

        [TestMethod]
        public void XPathSelectorFlowBlock_ReturnsOuterXml()
        {
            var startFlowBlock = CreateFlowBlock<StartFlowBlock>();
            var selector = CreateFlowBlock<XPathSelectorFlowBlock>(startFlowBlock);
            selector.XmlContent = "<root><Country><Code>DE</Code></Country></root>";
            selector.XPath = "//Country";
            selector.ResultMode = XPathResultMode.OuterXml;

            var runtime = new FlowBloxUnitTestRuntime(_project);

            Assert.IsTrue(selector.Execute(runtime, null));
            CollectionAssert.AreEqual(
                new[] { "<Country><Code>DE</Code></Country>" },
                GetResultValues(selector));
        }

        [TestMethod]
        public void XmlDocumentXPathSelectorFlowBlock_ReturnsAttributeValuesFromManagedDocument()
        {
            var startFlowBlock = CreateFlowBlock<StartFlowBlock>();
            var document = CreateFlowBlock<XmlDocumentFlowBlock>(startFlowBlock);
            document.XmlContent = "<root><item id=\"one\"/><item id=\"two\"/></root>";

            var selector = CreateFlowBlock<XmlDocumentXPathSelectorFlowBlock>(document);
            selector.AssociatedXmlDocument = document;
            selector.XPath = "//item/@id";

            var runtime = new FlowBloxUnitTestRuntime(_project);

            Assert.IsTrue(document.Execute(runtime, null));
            Assert.IsTrue(selector.Execute(runtime, null));
            CollectionAssert.AreEqual(new[] { "one", "two" }, GetResultValues(selector));
        }

        [TestMethod]
        public void XmlDocumentXPathSelectorFlowBlock_ReturnsOuterXmlFromManagedDocument()
        {
            var startFlowBlock = CreateFlowBlock<StartFlowBlock>();
            var document = CreateFlowBlock<XmlDocumentFlowBlock>(startFlowBlock);
            document.XmlContent = "<root><Country><Code>DE</Code></Country></root>";

            var selector = CreateFlowBlock<XmlDocumentXPathSelectorFlowBlock>(document);
            selector.AssociatedXmlDocument = document;
            selector.XPath = "//Country";
            selector.ResultMode = XPathResultMode.OuterXml;

            var runtime = new FlowBloxUnitTestRuntime(_project);

            Assert.IsTrue(document.Execute(runtime, null));
            Assert.IsTrue(selector.Execute(runtime, null));
            CollectionAssert.AreEqual(
                new[] { "<Country><Code>DE</Code></Country>" },
                GetResultValues(selector));
        }

        private static string[] GetResultValues(FlowBlox.Core.Models.FlowBlocks.Base.BaseSingleResultFlowBlock selector)
        {
            return selector.GridElementResult.Results
                .SelectMany(x => x.FieldValueMappings)
                .Select(x => x.Value)
                .ToArray();
        }
    }
}
