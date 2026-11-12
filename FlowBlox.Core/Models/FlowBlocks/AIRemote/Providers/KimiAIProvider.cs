using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Enums;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers
{
    [Display(Name = "KimiAIProvider_DisplayName", Description = "KimiAIProvider_Description", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("KimiAIProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class KimiAIProvider : OpenAICompatibleProviderBase
    {
        public override string ProviderType => "Kimi";

        protected override string ProviderDisplayName => "Kimi";

        public KimiAIProvider() : base("https://api.moonshot.ai/v1", "kimi-k3.1-pro")
        {
            EstimatedSystemPromptCacheSavingsRate = 0.50d;
        }

        protected override string GetReasoningEffortValue()
        {
            return GetRuntimeReasoningEffort() == AIReasoningEffort.Low
                ? "low"
                : "high";
        }

        protected override void ConfigureExecutionSettings(OpenAIPromptExecutionSettings settings, AIChatRequest request)
        {
            var model = FlowBloxFieldHelper.ReplaceFieldsInString(request.Model);
            if (string.IsNullOrWhiteSpace(model))
                model = this.Resolve(owner => owner.DefaultModel);

            // K3.1 Pro (including Highspeed) fixes temperature server-side.
            if (model.StartsWith("kimi-k3.1-pro", StringComparison.OrdinalIgnoreCase))
                settings.Temperature = null;
        }
    }
}
