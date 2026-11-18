using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.FlowBlocks.Web.WebBrowser;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;

namespace FlowBloxTest.Models.Project;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class LegacyWebDownloadImportTests
{
    [TestMethod]
    public void Import_LegacyDownloadResultCollection_PreservesDestinationAndField()
    {
        const string json = """
            {
              "Name": "Download",
              "ResultFields": {
                "$type": "System.Collections.ObjectModel.ObservableCollection`1[[FlowBlox.Core.Models.FlowBlocks.Base.ResultFieldByEnumValue`1[[FlowBlox.Core.Models.FlowBlocks.WebBrowser.WebDownloadDestinations, FlowBlox.Core]], FlowBlox.Core]], System.ObjectModel",
                "$values": [{
                  "$type": "FlowBlox.Core.Models.FlowBlocks.Base.ResultFieldByEnumValue`1[[FlowBlox.Core.Models.FlowBlocks.WebBrowser.WebDownloadDestinations, FlowBlox.Core]], FlowBlox.Core",
                  "EnumValue": "DownloadPath",
                  "ResultField": { "Name": "DownloadedFile" }
                }]
              }
            }
            """;

        var block = JsonConvert.DeserializeObject<WebDownloadFlowBlock>(json, JsonSettings.ProjectImport(null));

        Assert.IsNotNull(block);
        var result = block.ResultFields.Single();
        Assert.AreEqual(WebDownloadDestinations.DownloadPath, result.EnumValue);
        Assert.AreEqual("DownloadedFile", result.ResultField.Name);
    }
}
