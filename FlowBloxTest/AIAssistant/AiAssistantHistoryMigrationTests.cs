using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AiAssistantHistoryMigrationTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("SyntheticComponent")]
    public void LegacyProjectHashIsMigratedAndSavedAsContextHash(string componentScope)
    {
        var legacy = new JObject
        {
            ["ComponentScope"] = componentScope,
            ["LastProjectJsonHash"] = "LegacyHash"
        };

        var history = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(legacy.ToString());
        Assert.AreEqual("LegacyHash", history.LastContextJsonHash);

        var saved = JObject.Parse(JsonConvert.SerializeObject(history));
        Assert.AreEqual("LegacyHash", saved.Value<string>("LastContextJsonHash"));
        Assert.IsFalse(saved.ContainsKey("LastProjectJsonHash"));
    }

    [TestMethod]
    [DataRow("{\"LastContextJsonHash\":\"CurrentHash\",\"LastProjectJsonHash\":\"LegacyHash\"}")]
    [DataRow("{\"LastProjectJsonHash\":\"LegacyHash\",\"LastContextJsonHash\":\"CurrentHash\"}")]
    public void CurrentContextHashTakesPrecedenceRegardlessOfPropertyOrder(string json)
    {
        var history = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(json);

        Assert.AreEqual("CurrentHash", history.LastContextJsonHash);
    }
}
