using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers
{
    [Display(Name = "OpenAIProvider_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("OpenAIProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class OpenAIProvider : OpenAICompatibleProviderBase
    {
        [Display(Name = "OpenAIProvider_OrganizationId", Description = "OpenAIProvider_OrganizationId_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 10)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string OrganizationId { get; set; }


        public override string ProviderType => "OpenAI";

        protected override string ProviderDisplayName => "OpenAI";

        protected override string? OrganizationIdForRequest => OrganizationId;

        public OpenAIProvider() : base("https://api.openai.com/v1", "gpt-6.1-sol")
        {
            EstimatedSystemPromptCacheSavingsRate = 0.80d;
        }
    }
}