using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers
{
    [Display(Name = "MistralAIProvider_DisplayName", Description = "MistralAIProvider_Description", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("MistralAIProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class MistralAIProvider : OpenAICompatibleProviderBase
    {
        public override string ProviderType => "Mistral";

        protected override string ProviderDisplayName => "Mistral";

        public MistralAIProvider() : base("https://api.mistral.ai/v1", "mistral-large-4")
        {
        }
    }
}
