using FlowBlox.AIAssistant.Constants;
using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Builder
{
    internal sealed class AssistantTokenBudget
    {
        public static AssistantTokenBudget Create(AssistantConfiguration configuration, ModelPricing? pricing, double calibration = 1d)
        {
            var modelLimit = pricing?.MaxInputTokens ?? pricing?.MaxTokens ?? int.MaxValue;
            var contextLimit = Math.Max(AssistantConfigurationLimits.MinContextTokens, configuration.MaxContextTokens);
            var responseReserve = Math.Max(configuration.MaxTokens ?? 0, configuration.ReservedResponseTokens);
            return new AssistantTokenBudget
            {
                MaxContextTokens = contextLimit == 0 ? modelLimit : Math.Min(modelLimit, contextLimit),
                ReservedResponseTokens = Math.Max(AssistantConfigurationLimits.MinReservedResponseTokens, responseReserve),
                ApproximateCharactersPerToken = Math.Clamp(
                    configuration.ApproximateCharactersPerToken,
                    AssistantConfigurationLimits.MinApproximateCharactersPerToken,
                    AssistantConfigurationLimits.MaxApproximateCharactersPerToken),
                TokenEstimateMultiplier = calibration
            };
        }

        public double TokenEstimateMultiplier { get; set; } = 1d;
        public int MaxContextTokens { get; set; }
        public int ReservedResponseTokens { get; set; }
        public int ApproximateCharactersPerToken { get; set; }

        public int EstimateTokens(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var charsPerToken = Math.Max(AssistantConfigurationLimits.MinApproximateCharactersPerToken, ApproximateCharactersPerToken);
            return Math.Max(1, (int)Math.Ceiling(text.Length / (double)charsPerToken * TokenEstimateMultiplier));
        }
    }
}