using FlowBlox.AIAssistant.Tools.Util;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class SnapshotJsonFilterTests
{
    [TestMethod]
    public void MultiplePaths_ReturnCompleteSubtreesWithoutUnrequestedSiblings()
    {
        var snapshot = JObject.Parse("""
            {"Mappings":[{"Name":"A","Options":{"Keys":[1,2,3]}},{"Name":"B"}],
             "Association":{"Settings":{"Limit":5,"Mode":"All"},"Other":"omit"},"Unrelated":"omit"}
            """);

        var result = SnapshotJsonFilter.Select(snapshot, new JArray("$.Mappings", "$.Association.Settings"));

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(JToken.DeepEquals(snapshot["Mappings"], result[0]!["value"]));
        Assert.IsTrue(JToken.DeepEquals(snapshot["Association"]!["Settings"], result[1]!["value"]));
        Assert.IsFalse(result.ToString().Contains("omit", StringComparison.Ordinal));
        result[0]!["value"]![0]!["Name"] = "Changed copy";
        Assert.AreEqual("A", snapshot["Mappings"]![0]!["Name"]!.Value<string>());
    }

    [TestMethod]
    public void Wildcard_ReportsOriginalArrayIndicesEvenForIdenticalValues()
    {
        var snapshot = JObject.Parse("""{"Items":[{"Name":"same"},{"Other":1},{"Name":"same"}]}""");

        var result = SnapshotJsonFilter.Select(snapshot, new JArray("$.Items[*].Name"));

        CollectionAssert.AreEqual(new[] { "$.Items[0].Name", "$.Items[2].Name" }, result.Select(item => item["path"]!.Value<string>()).ToArray());
        Assert.IsTrue(result.All(item => item["value"]!.Value<string>() == "same"));
    }

    [TestMethod]
    public void DuplicateAndOverlappingQueries_ReturnTheCompleteParentOnlyOnce()
    {
        var snapshot = JObject.Parse("""{"Items":[{"Name":"A","Settings":{"X":1}},{"Name":"B"}]}""");

        var result = SnapshotJsonFilter.Select(snapshot, new JArray("$.Items[0].Name", "$.Items", "$.Items", "$..Name"));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("$.Items", result[0]!["path"]!.Value<string>());
        Assert.IsTrue(JToken.DeepEquals(snapshot["Items"], result[0]!["value"]));
    }

    [TestMethod]
    public void CommasAndQuotedPropertyNames_AreNotTreatedAsListSeparators()
    {
        var snapshot = JObject.Parse("""{"first,second":{"Name":"kept"},"Other":{"Value":2}}""");

        var result = SnapshotJsonFilter.Select(snapshot, new JArray("$['first,second']", "$['Other']['Value']"));

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(JToken.DeepEquals(snapshot.SelectToken(result[0]!["path"]!.Value<string>()!), result[0]!["value"]));
        Assert.AreEqual("kept", result[0]!["value"]!["Name"]!.Value<string>());
        Assert.AreEqual(2, result[1]!["value"]!.Value<int>());
    }

    [TestMethod]
    public void InvalidFilterOrPath_FailsInsteadOfReturningTheFullSnapshot()
    {
        var snapshot = JObject.Parse("""{"Name":"Reader"}""");
        JToken[] invalid =
        [
            JValue.CreateNull(), new JValue("$.Name,$.Other"), new JArray(), new JArray(5),
            new JArray(""), new JArray("/Name"), new JArray("$.Items["),
            new JArray("$.Name", "$.Missing")
        ];

        foreach (var filter in invalid)
            Assert.Throws<ArgumentException>(() => SnapshotJsonFilter.Select(snapshot, filter), filter.ToString());
    }

    [TestMethod]
    public void RootSelection_KeepsCompleteJsonAndSuppressesCoveredMatches()
    {
        var snapshot = JObject.Parse("""{"Name":"Reader","Settings":{"X":1}}""");

        var result = SnapshotJsonFilter.Select(snapshot, new JArray("$.Settings.X", "$"));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("$", result[0]!["path"]!.Value<string>());
        Assert.IsTrue(JToken.DeepEquals(snapshot, result[0]!["value"]));
    }
}
