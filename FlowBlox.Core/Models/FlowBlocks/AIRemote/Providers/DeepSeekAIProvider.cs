using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Enums;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers
{
    [Display(Name = "DeepSeekAIProvider_DisplayName", Description = "DeepSeekAIProvider_Description", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("DeepSeekAIProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class DeepSeekAIProvider : OpenAICompatibleProviderBase
    {
        public override string ProviderType => "DeepSeek";

        protected override string ProviderDisplayName => "DeepSeek";

        public DeepSeekAIProvider() : base("https://api.deepseek.com", "deepseek-flash")
        {
            EstimatedSystemPromptCacheSavingsRate = 0.60d;
        }

        protected override string GetReasoningEffortValue()
        {
            return GetRuntimeReasoningEffort() == AIReasoningEffort.Low
                ? "low"
                : "high";
        }

        protected override void ConfigureExecutionSettings(OpenAIPromptExecutionSettings settings, AIChatRequest request)
        {
#pragma warning disable SKEXP0010
            settings.ExtraBody = new Dictionary<string, object>
            {
                ["thinking"] = new Dictionary<string, object>
                {
                    ["type"] = GetRuntimeReasoningEffort() == AIReasoningEffort.None
                        ? "disabled"
                        : "enabled"
                }
            };
#pragma warning restore SKEXP0010
        }

        public override bool TryParseInstruction(
            string output,
            Exception? primaryParseException,
            out JObject instructionJson)
        {
            return DeepSeekDsmlInstructionParser.TryParse(output, out instructionJson);
        }
    }
}
