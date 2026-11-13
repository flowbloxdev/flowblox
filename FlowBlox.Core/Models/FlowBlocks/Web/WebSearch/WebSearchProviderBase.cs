using FlowBlox.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Web.WebSearch
{
    [Display(Name = "WebSearchProviderBase_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("WebSearchProviderBase_DisplayName_Plural", typeof(FlowBloxTexts))]
    public abstract class WebSearchProviderBase : ManagedObject
    {
        internal virtual bool SupportsCountry => false;
        internal virtual bool SupportsSearchLanguage => false;
        internal virtual bool SupportsSafeSearch => false;

        [Required]
        [Display(Name = "WebSearchProviderBase_ApiKey", Description = "WebSearchProviderBase_ApiKey_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(IsPassword = true)]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        [Display(Name = "WebSearchProviderBase_BaseUrl", Description = "WebSearchProviderBase_BaseUrl_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public string BaseUrl { get; set; } = string.Empty;

        [Range(1, 300)]
        [Display(Name = "WebSearchProviderBase_TimeoutSeconds", Description = "WebSearchProviderBase_TimeoutSeconds_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public int TimeoutSeconds { get; set; } = 30;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.web, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.web, 32, SKColors.SteelBlue);

        public IReadOnlyList<WebSearchResult> Search(WebSearchRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Query))
                throw new ValidationException("The web search query is empty.");

            return SearchCoreAsync(request, cancellationToken).GetAwaiter().GetResult();
        }

        protected string ResolveApiKey()
        {
            var value = this.Resolve(owner => owner.ApiKey, required: true).Trim();
            return value;
        }

        protected string ResolveBaseUrl()
        {
            var value = this.Resolve(owner => owner.BaseUrl, required: true).Trim().TrimEnd('/');
            return value;
        }

        protected abstract Task<IReadOnlyList<WebSearchResult>> SearchCoreAsync(
            WebSearchRequest request,
            CancellationToken cancellationToken);
    }
}
