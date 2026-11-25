using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers
{
    [Display(Name = "XAIProvider_DisplayName", Description = "XAIProvider_Description", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("XAIProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class XAIProvider : OpenAICompatibleProviderBase
    {
        public override string ProviderType => "xAI";

        protected override string ProviderDisplayName => "xAI";

        public XAIProvider() : base("https://api.x.ai/v1", "grok-4.7")
        {
        }
    }
}
