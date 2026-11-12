using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Web.WebSearch;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Web
{
    [Display(Name = "WebSearchFlowBlock_DisplayName", Description = "WebSearchFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public sealed class WebSearchFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "WebSearchFlowBlock_Provider", Description = "WebSearchFlowBlock_Provider_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association, SelectionFilterMethod = nameof(GetPossibleProviders), SelectionDisplayMember = nameof(WebSearchProviderBase.Name))]
        public WebSearchProviderBase Provider { get; set; }

        [Required]
        [Display(Name = "WebSearchFlowBlock_Query", Description = "WebSearchFlowBlock_Query_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Query { get; set; } = string.Empty;

        [Range(1, 20)]
        [Display(Name = "WebSearchFlowBlock_MaxResults", Description = "WebSearchFlowBlock_MaxResults_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int MaxResults { get; set; } = 10;

        [ActivationCondition(ActivationMethod = nameof(IsCountryActive))]
        [Display(Name = "WebSearchFlowBlock_Country", Description = "WebSearchFlowBlock_Country_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Country { get; set; } = string.Empty;

        [ActivationCondition(ActivationMethod = nameof(IsSearchLanguageActive))]
        [Display(Name = "WebSearchFlowBlock_SearchLanguage", Description = "WebSearchFlowBlock_SearchLanguage_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string SearchLanguage { get; set; } = string.Empty;

        [ActivationCondition(ActivationMethod = nameof(IsSafeSearchActive))]
        [Display(Name = "WebSearchFlowBlock_SafeSearch", Description = "WebSearchFlowBlock_SafeSearch_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public WebSearchSafeSearch SafeSearch { get; set; } = WebSearchSafeSearch.Moderate;

        [Display(Name = "WebSearchFlowBlock_ResultFields", Description = "WebSearchFlowBlock_ResultFields_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<WebSearchDestinations>> ResultFields { get; set; } = [];

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.search_web, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.search_web, 32, SKColors.SteelBlue);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<FieldElement> Fields => ResultFields
            .Where(x => x.EnumValue != null)
            .Select(x => x.ResultField)
            .ExceptNull()
            .ToList();

        public IEnumerable<WebSearchProviderBase> GetPossibleProviders() =>
            FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<WebSearchProviderBase>();

        private bool IsCountryActive() => Provider?.SupportsCountry == true;
        private bool IsSearchLanguageActive() => Provider?.SupportsSearchLanguage == true;
        private bool IsSafeSearchActive() => Provider?.SupportsSafeSearch == true;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Provider));
            properties.Add(nameof(Query));
            properties.Add(nameof(MaxResults));
            return properties;
        }

        public override void OnAfterCreate()
        {
            if (!ResultFields.Any())
            {
                CreateDestinationResultField(ResultFields, WebSearchDestinations.Title);
                CreateDestinationResultField(ResultFields, WebSearchDestinations.Url);
                CreateDestinationResultField(ResultFields, WebSearchDestinations.Description);
            }

            base.OnAfterCreate();
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (Provider == null)
                    throw new InvalidOperationException("No web search provider has been configured.");
                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                var query = this.Resolve(owner => owner.Query).Trim();
                if (string.IsNullOrWhiteSpace(query))
                {
                    GenerateResult(runtime);
                    return;
                }

                var results = Provider.Search(
                    new WebSearchRequest
                    {
                        Query = query,
                        MaxResults = Math.Clamp(MaxResults, 1, 20),
                        Country = this.Resolve(owner => owner.Country).Trim(),
                        SearchLanguage = this.Resolve(owner => owner.SearchLanguage).Trim(),
                        SafeSearch = SafeSearch
                    },
                    runtime.GetCancellationToken());

                var rows = results.Select((result, index) =>
                    new ResultFieldByEnumValueResultBuilder<WebSearchDestinations>()
                        .For(WebSearchDestinations.Title, result.Title)
                        .For(WebSearchDestinations.Url, result.Url)
                        .For(WebSearchDestinations.Description, result.Description)
                        .For(WebSearchDestinations.Age, result.Age)
                        .For(WebSearchDestinations.PageAge, result.PageAge)
                        .For(WebSearchDestinations.Language, result.Language)
                        .For(WebSearchDestinations.Rank, (index + 1).ToString(CultureInfo.InvariantCulture))
                        .For(WebSearchDestinations.Query, query)
                        .Build(ResultFields));

                GenerateResult(runtime, rows);
            });
        }
    }
}
