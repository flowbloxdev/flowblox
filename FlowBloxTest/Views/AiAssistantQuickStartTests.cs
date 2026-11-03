using FlowBlox.Core.Util;
using FlowBlox.UICore.ViewModels;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.Core.Models.Components;

namespace FlowBloxTest.Views;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AiAssistantQuickStartTests
{
    [STATestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow("$Options::Tests.AiAssistant.ApiKey", false)]
    [DataRow("test-key", true)]
    [DataRow("$Options::Tests.AiAssistant.ApiKey", true)]
    public void ProviderReadiness_ResolvesKeysAndRejectsInvalidConfigurationWithoutLosingDraft(string? apiKey, bool ready)
    {
        var options = FlowBloxOptions.GetOptionInstance();
        var configuration = options.GetOption("AI.AssistantConfiguration");
        var previousConfiguration = configuration.PersistentValue;
        const string keyOptionName = "Tests.AiAssistant.ApiKey";
        options.OptionCollection.TryGetValue(keyOptionName, out var previousKeyOption);
        try
        {
            options.OptionCollection[keyOptionName] = new OptionElement(
                keyOptionName,
                ready ? "test-key" : string.Empty,
                "Test API key",
                OptionElement.OptionType.Text,
                isPlaceholderEnabled: true);
            configuration.PersistentValue = AssistantConfigurationJson.Serialize(new AssistantConfiguration
            {
                Provider = apiKey == null ? null! : new OpenAIProvider { ApiKey = apiKey }
            });
            using var viewModel = new AiAssistantChatViewModel();
            Assert.AreEqual(!ready, viewModel.ShowProviderConfigurationWarning);
            if (ready)
                return;

            const string draft = "  Create a flow from my requirements.  ";
            viewModel.CurrentInput = draft;

            Assert.IsTrue(viewModel.SubmitCommand.CanExecute(null));
            viewModel.SubmitCommand.Execute(null);

            Assert.AreEqual(draft, viewModel.CurrentInput);
            Assert.IsFalse(viewModel.IsBusy);
            Assert.AreEqual(0, viewModel.Transcript.Count);
            Assert.IsTrue(viewModel.ShowProviderConfigurationWarning);
        }
        finally
        {
            configuration.PersistentValue = previousConfiguration;
            if (previousKeyOption == null)
                options.OptionCollection.Remove(keyOptionName);
            else
                options.OptionCollection[keyOptionName] = previousKeyOption;
        }
    }
}
