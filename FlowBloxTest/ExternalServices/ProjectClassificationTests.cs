using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.ExternalServices;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ProjectClassificationTests
{
    [TestMethod]
    [DataRow("\"Standard\"")]
    [DataRow("\"standard\"")]
    [DataRow("0")]
    public void LegacyMetadata_IsReadAsSolutionAndWrittenWithCanonicalName(string legacyValue)
    {
        var project = JsonConvert.DeserializeObject<FbProjectResult>("{\"Classification\":" + legacyValue + "}")!;
        var change = new FbProjectChangeRequest { Classification = project.Classification };

        Assert.AreEqual(FbProjectClassification.Solution, project.Classification);
        Assert.AreEqual("Solution", JObject.Parse(JsonConvert.SerializeObject(change)).Value<string>("Classification"));
    }

    [TestMethod]
    [DataRow("Solution", FbProjectClassification.Solution)]
    [DataRow("Function", FbProjectClassification.Function)]
    public void CanonicalMetadata_RetainsClassificationAcrossRoundTrip(string value, FbProjectClassification expected)
    {
        var project = JsonConvert.DeserializeObject<FbProjectResult>("{\"Classification\":\"" + value + "\"}")!;
        var serialized = JObject.Parse(JsonConvert.SerializeObject(project));

        Assert.AreEqual(expected, project.Classification);
        Assert.AreEqual(value, serialized.Value<string>("Classification"));
    }

    [TestMethod]
    public void UnknownClassification_IsRejectedInsteadOfBecomingSolution()
    {
        Assert.ThrowsExactly<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<FbProjectResult>("{\"Classification\":\"Unknown\"}"));
    }
}
