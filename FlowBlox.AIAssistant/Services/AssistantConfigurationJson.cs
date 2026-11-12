using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Services
{
    public static class AssistantConfigurationJson
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static AssistantConfigurationParseResult Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new AssistantConfigurationParseResult();

            try
            {
                var json = JObject.Parse(raw);
                var serializer = JsonSerializer.Create(_settings);
                var configuration = json.ToObject<AssistantConfiguration>(serializer)
                    ?? new AssistantConfiguration();

                // Preserve existing single-provider configurations without storing an object reference.
                if (configuration.Providers.Count == 0 && json["Provider"] is JObject legacyProvider)
                {
                    var provider = legacyProvider.ToObject<AIProviderBase>(serializer);
                    if (provider != null)
                    {
                        if (string.IsNullOrWhiteSpace(provider.Name))
                            provider.Name = provider.ProviderType;
                        configuration.Providers.Add(provider);
                    }
                }
                return new AssistantConfigurationParseResult
                {
                    Configuration = configuration
                };
            }
            catch (Exception ex)
            {
                return new AssistantConfigurationParseResult
                {
                    Error = $"Invalid JSON in option 'AI.AssistantConfiguration': {ex.Message}"
                };
            }
        }

        public static string Serialize(AssistantConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return JsonConvert.SerializeObject(configuration, _settings);
        }
    }
}
