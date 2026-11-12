using FlowBlox.AIAssistant.Tools.Util;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class ProtocolEntryWindowingTests
    {
        [TestMethod]
        public void TakeLastInOriginalOrder_DropsOldestEntriesAndPreservesOrder()
        {
            var source = new JArray(1, 2, 3, 4, 5, 6);

            var result = ProtocolEntryWindowing.TakeLastInOriginalOrder(source, 2);

            CollectionAssert.AreEqual(new[] { 5, 6 }, result.Values<int>().ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, source.Values<int>().ToArray());
        }

    }
}
