using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;

namespace FlowBlox.AIAssistant
{
    public class AssistantOptionsRegistration : IOptionsRegistration
    {
        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "AI.PricingCatalogueRefreshHours",
                "72",
                "Refresh interval in hours for the AI model pricing catalogue (1-8760).",
                OptionElement.OptionType.Text));

            var defaultConfiguration = new AssistantConfiguration();
            defaultConfiguration.Providers.Add(new OpenAIProvider { Name = "OpenAI" });
            defaults.Add(new OptionElement(
                "AI.AssistantConfiguration",
                AssistantConfigurationJson.Serialize(defaultConfiguration),
                "JSON configuration for FlowBlox AI Assistant.",
                OptionElement.OptionType.Text));

            defaults.Add(new OptionElement(
                "AI.CommuncationProtocolDir",
                @"$Options::Paths.LocalAppDataDir\logs\ai_assistant_protocol",
                "Directory path for AI assistant communication protocol files.",
                OptionElement.OptionType.Text));

            defaults.Add(new OptionElement(
                "AI.AssistantHistoryDirectory",
                @"$Options::Paths.LocalAppDataDir\ai_assistant_histories",
                "Directory path for AI assistant history files.",
                OptionElement.OptionType.Text));
        }
    }
}
