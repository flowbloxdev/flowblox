using System.ComponentModel.DataAnnotations;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantProviderConfigurationTests
{
    [TestMethod]
    public void AttachingProviders_SelectsFirstWhenEmptyAndPreservesExistingSelection()
    {
        var configuration = new AssistantConfiguration();
        var first = new OpenAIProvider { Name = "First" };
        var second = new DeepSeekAIProvider { Name = "Second" };
        var notifications = new List<string>();
        configuration.PropertyChanged += (_, args) => notifications.Add(args.PropertyName!);

        configuration.Providers.Add(first);
        Assert.AreEqual("First", configuration.SelectedProviderName);
        Assert.AreSame(first, configuration.SelectedProvider);
        configuration.Providers.Add(second);
        Assert.AreSame(first, configuration.SelectedProvider);
        configuration.SelectedProviderName = "Second";
        Assert.AreSame(second, configuration.SelectedProvider);
        CollectionAssert.AreEqual(new[] { "First", "Second" }, configuration.GetProviderNames().ToArray());
        Assert.Contains(nameof(configuration.Providers), notifications);
        Assert.Contains(nameof(configuration.SelectedProvider), notifications);
    }

    [TestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("Missing", false)]
    [DataRow("Existing", true)]
    public void Validation_RequiresAConnectedProviderName(string? selectedName, bool valid)
    {
        var configuration = new AssistantConfiguration();
        var provider = new OpenAIProvider { Name = "Existing" };
        configuration.Providers.Add(provider);
        configuration.SelectedProviderName = selectedName;

        Assert.AreEqual(valid, !configuration.ValidateObject().ContainsKey(nameof(configuration.SelectedProviderName)));
        Assert.AreSame(valid ? provider : null, configuration.SelectedProvider);
    }

    [TestMethod]
    public void RemovingSelectedProvider_DoesNotFallBackToAnotherProvider()
    {
        var configuration = new AssistantConfiguration();
        var provider = new OpenAIProvider { Name = "Existing" };
        configuration.Providers.Add(provider);
        configuration.Providers.Add(new DeepSeekAIProvider { Name = "Other" });
        configuration.Providers.Remove(provider);

        Assert.AreEqual("Existing", configuration.SelectedProviderName);
        Assert.IsNull(configuration.SelectedProvider);
        Assert.IsTrue(configuration.ValidateObject().ContainsKey(nameof(configuration.SelectedProviderName)));
    }

    [TestMethod]
    public void RoundTrip_StoresOnlyNameAndListAndResolvesSelectedInstance()
    {
        var configuration = new AssistantConfiguration();
        configuration.Providers.Add(new OpenAIProvider { Name = "First" });
        configuration.Providers.Add(new DeepSeekAIProvider { Name = "Second" });
        configuration.SelectedProviderName = "Second";

        var json = AssistantConfigurationJson.Serialize(configuration);
        var document = JObject.Parse(json);
        Assert.IsNull(document[nameof(configuration.SelectedProvider)]);
        Assert.IsNull(document["Provider"]);
        Assert.IsFalse(json.Contains("\"$ref\"", StringComparison.Ordinal));
        var parsed = AssistantConfigurationJson.Parse(json);
        Assert.IsFalse(parsed.HasError, parsed.Error);
        Assert.HasCount(2, parsed.Configuration!.Providers);
        Assert.AreSame(parsed.Configuration.Providers[1], parsed.Configuration.SelectedProvider);
        Assert.IsInstanceOfType<DeepSeekAIProvider>(parsed.Configuration.SelectedProvider);
    }

    [TestMethod]
    public void Parse_PreservesLegacySingleProviderIncludingUnnamedDefaults()
    {
        var provider = new OpenAIProvider { DefaultModel = "configured-model" };
        var legacy = JsonConvert.SerializeObject(
            new { Provider = (AIProviderBase)provider },
            new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });

        var parsed = AssistantConfigurationJson.Parse(legacy);

        Assert.IsFalse(parsed.HasError, parsed.Error);
        Assert.HasCount(1, parsed.Configuration!.Providers);
        Assert.AreSame(parsed.Configuration.Providers[0], parsed.Configuration.SelectedProvider);
        Assert.AreEqual("configured-model", parsed.Configuration.SelectedProvider.DefaultModel);
        Assert.IsEmpty(parsed.Configuration.ValidateObject());
    }
}
