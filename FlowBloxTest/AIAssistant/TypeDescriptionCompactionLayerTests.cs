using System.Text;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class TypeDescriptionCompactionLayerTests
{
    private const string OptionName = "AI.CompactTypeDescriptions";
    private string? _previousValue;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        var options = FlowBloxOptions.GetOptionInstance();
        var option = options.GetOption(OptionName);
        if (option == null)
        {
            option = new OptionElement(OptionName, "true", "", OptionElement.OptionType.Boolean);
            options.OptionCollection.Add(OptionName, option);
        }

        _previousValue = option.PersistentValue;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowBloxOptions.GetOptionInstance().GetOption(OptionName).PersistentValue = _previousValue;
        ToolHandlerUtilities.ClearSessionCache();
    }

    [TestMethod]
    [DataRow(typeof(TableReaderFlowBlock), false)]
    [DataRow(typeof(WebBrowserFlowBlock), false)]
    [DataRow(typeof(ExecuteProjectFlowBlock), false)]
    [DataRow(typeof(SQLTable), true)]
    public async Task CompactResponses_RoundTripEntireOriginalPayload_AndReduceBytes(Type type, bool managed)
    {
        var original = await ReadType(type, managed, false);
        var compact = await ReadType(type, managed, true);
        Assert.IsNotNull(compact["propertyColumns"]);
        Assert.IsTrue(JToken.DeepEquals(original, Expand(compact)), type.FullName);

        var originalBytes = Encoding.UTF8.GetByteCount(original.ToString(Formatting.None));
        var compactBytes = Encoding.UTF8.GetByteCount(compact.ToString(Formatting.None));
        TestContext.WriteLine($"{type.Name}: {originalBytes} -> {compactBytes} UTF-8 bytes ({100d * (originalBytes - compactBytes) / originalBytes:F1}% reduction)");
        Assert.IsTrue(compactBytes < originalBytes, "The schema overhead must be included in the size comparison.");
    }

    [TestMethod]
    public async Task CompactPromotions_RoundTripSystemDeclarations_WithoutChangingRetrievalBehavior()
    {
        var original = await ReadPromotions(false);
        var compact = await ReadPromotions(true);
        CollectionAssert.AreEquivalent(original.Keys.ToArray(), compact.Keys.ToArray());
        foreach (var key in original.Keys)
        {
            Assert.IsTrue(JToken.DeepEquals(original[key], Expand(compact[key])), key);
        }
    }

    [TestMethod]
    public async Task CompactEnum_RetainsOriginalPayloadWithoutPropertySchema()
    {
        var original = await ReadType(typeof(DayOfWeek), false, false);
        var compact = await ReadType(typeof(DayOfWeek), false, true);
        Assert.IsTrue(JToken.DeepEquals(original, compact));
    }

    [TestMethod]
    public void CompactLayer_PreservesUnknownMetadataAndEscaping_WithoutMutatingTheSource()
    {
        var payload = new JObject
        {
            ["kind"] = new JObject
            {
                ["properties"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "Example",
                        ["type"] = "System.String",
                        ["flags"] = "NWS",
                        ["majorTypeDescriptor"] = "SimpleProperty",
                        ["canUpdateViaParentPath"] = true,
                        ["displayName"] = "Example",
                        ["description"] = "A quote: \"; a newline:\n; separator: |; Unicode: ä",
                        ["customAnnotation"] = new JObject
                        {
                            ["properties"] = new JArray("first", "second"),
                            ["nullable"] = JValue.CreateNull()
                        }
                    }
                }
            }
        };
        var original = (JObject)payload.DeepClone();
        var compact = TypeDescriptionCompactionLayer.Compact(payload);
        var transmitted = JObject.Parse(compact.ToString(Formatting.None));
        Assert.IsTrue(JToken.DeepEquals(original, Expand(transmitted)));
        Assert.IsTrue(JToken.DeepEquals(original, payload));
        Assert.IsTrue(JToken.DeepEquals(compact, TypeDescriptionCompactionLayer.Compact(compact)));
    }

    private static async Task<JObject> ReadType(Type type, bool managed, bool compact)
    {
        FlowBloxOptions.GetOptionInstance().GetOption(OptionName).PersistentValue = compact.ToString();
        ToolHandlerUtilities.ClearSessionCache();
        ToolHandlerBase handler = managed ? new GetManagedObjectKindsInfoHandler() : new GetTypeKindsInfoHandler();
        var response = await handler.HandleAsync(new JObject { ["typeFullName"] = type.FullName }, CancellationToken.None);
        Assert.IsTrue(response.Ok, response.Error);
        return JObject.Parse(response.Result.ToString(Formatting.None));
    }

    private static async Task<Dictionary<string, JObject>> ReadPromotions(bool compact)
    {
        await ReadType(typeof(WebBrowserFlowBlock), false, compact);
        var handler = new GetTypeKindsInfoHandler();
        var args = new JObject { ["typeFullName"] = typeof(WebBrowserFlowBlock).FullName };
        var repeated = await handler.HandleAsync(args, CancellationToken.None);
        Assert.IsTrue(repeated.Ok, repeated.Error);
        Assert.IsNull(repeated.Result["kind"]);
        Assert.IsNull(repeated.Result["propertyColumns"]);
        var declarations = ToolHandlerUtilities.CurrentContentCache.GetSystemMessages()
            .Where(message => message.Kind == AssistantSystemMessageKind.TypeInfo)
            .ToDictionary(message => message.Key, message => JObject.Parse(message.Content));
        Assert.IsTrue(declarations.Count > 0);
        await handler.HandleAsync(args, CancellationToken.None);
        Assert.AreEqual(declarations.Count, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
        return declarations;
    }

    // Independent reader: only the transmitted schema determines column names.
    internal static JObject Expand(JObject payload)
    {
        var result = (JObject)payload.DeepClone();
        var columns = result["propertyColumns"]?.Values<string>().ToArray();
        if (columns == null)
            return result;

        var kinds = new List<JObject>();
        if (result["kind"] is JObject kind)
            kinds.Add(kind);
        kinds.AddRange(result["additionalTypeKindsInfo"]?.Children<JObject>() ?? []);
        foreach (var entry in kinds)
        {
            if (entry["properties"] is not JArray properties)
                continue;

            var expanded = new JArray();
            foreach (var row in properties.Children<JArray>())
            {
                Assert.AreEqual(columns.Length, row.Count);
                var property = (JObject)row.Last!.DeepClone();
                for (var index = 0; index < columns.Length - 1; index++)
                    property[columns[index]!] = row[index].DeepClone();
                expanded.Add(property);
            }

            entry["properties"] = expanded;
        }

        result.Remove("propertyColumns");
        return result;
    }
}
