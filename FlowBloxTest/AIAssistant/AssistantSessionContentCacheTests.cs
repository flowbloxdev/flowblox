using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantSessionContentCacheTests
{
    [TestMethod]
    public async Task RepeatedTypeRetrieval_ReturnsCompleteSchemasWithoutChangingSystemPrefix()
    {
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        ToolHandlerUtilities.SetCurrentSessionGuid(Guid.NewGuid().ToString());
        try
        {
            var handler = new GetTypeKindsInfoHandler();
            var arguments = new JObject { ["typeFullName"] = typeof(WebBrowserFlowBlock).FullName };
            var first = await handler.HandleAsync(arguments, CancellationToken.None);
            var second = await handler.HandleAsync(arguments, CancellationToken.None);
            Assert.IsTrue(first.Ok, first.Error);
            Assert.IsTrue(second.Ok, second.Error);
            Assert.IsTrue(JToken.DeepEquals(first.Result, second.Result));
            var expanded = TypeDescriptionCompactionLayerTests.Expand(second.Result);
            Assert.IsTrue(expanded["kind"]!["properties"]!.Children<JObject>().Any(x => x.Value<string>("name") == "Name"));
            Assert.AreEqual(0, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
        }
        finally
        {
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
        }
    }
}
