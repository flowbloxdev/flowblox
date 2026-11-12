using FlowBlox.Core.Models.FlowBlocks.Logic;
using FlowBlox.Core.Services;
using FlowBlox.Core.Util.Json;
using FlowBlox.Core.Util.Json.SerializationBinder;
using Newtonsoft.Json;

namespace FlowBloxTest.FlowBlocks.Logic;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FieldCopyFlowBlockTests
{
    [TestMethod]
    public void LegacyDistributor_ImportsItsCopiedFieldsAndModifiers()
    {
        const string json = """
            {
              "$type": "FlowBlox.Core.Models.FlowBlocks.Logic.DistributorFlowBlock, FlowBlox.Core",
              "Name": "LegacyCopy",
              "DisributedFields": {
                "$type": "System.Collections.ObjectModel.ObservableCollection`1[[FlowBlox.Core.Models.Components.FieldElement, FlowBlox.Core]], System.ObjectModel",
                "$values": [{
                  "Name": "Hash",
                  "Modifiers": {
                    "$values": [{
                      "$type": "FlowBlox.Core.Models.Components.Modifier.HashModifier, FlowBlox.Core",
                      "Algorithm": "SHA256",
                      "OutputEncoding": "HexLowerCase"
                    }]
                  }
                }]
              }
            }
            """;
        var settings = JsonSettings.ProjectImport([]);
        settings.SerializationBinder = new FlowBloxSerializationBinder([], new FlowBloxCoreLegacyTypeMappingService().GetLegacyTypeMappings());

        var copy = JsonConvert.DeserializeObject<FieldCopyFlowBlock>(json, settings)!;

        Assert.AreEqual("Hash", copy.Fields.Single().Name);
        Assert.IsInstanceOfType<FlowBlox.Core.Models.Components.Modifier.HashModifier>(copy.Fields.Single().Modifiers.Single());
    }
}
