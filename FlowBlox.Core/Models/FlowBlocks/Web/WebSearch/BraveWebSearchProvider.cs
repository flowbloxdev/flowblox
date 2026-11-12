using FlowBlox.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.FlowBlocks.Web.WebSearch
{
    [Display(Name = "BraveWebSearchProvider_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("BraveWebSearchProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class BraveWebSearchProvider : WebSearchProviderBase
    {
        internal override bool SupportsCountry => true;
        internal override bool SupportsSearchLanguage => true;
        internal override bool SupportsSafeSearch => true;

        public BraveWebSearchProvider()
        {
            BaseUrl = "https://api.search.brave.com/res/v1/web/search";
        }

        protected override async Task<IReadOnlyList<WebSearchResult>> SearchCoreAsync(
            WebSearchRequest request,
            CancellationToken cancellationToken)
        {
            var query = new Dictionary<string, string>
            {
                ["q"] = request.Query,
                ["count"] = Math.Clamp(request.MaxResults, 1, 20).ToString(CultureInfo.InvariantCulture),
                ["safesearch"] = request.SafeSearch.ToString().ToLowerInvariant(),
                ["result_filter"] = "web",
                ["text_decorations"] = "false"
            };
            AddIfNotEmpty(query, "country", request.Country);
            AddIfNotEmpty(query, "search_lang", request.SearchLanguage);

            var url = ResolveBaseUrl() + "?" + string.Join("&", query.Select(x =>
                $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

            var timeoutSeconds = this.Resolve(provider => provider.TimeoutSeconds);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Add("X-Subscription-Token", ResolveApiKey());

            using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Brave Search request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    inner: null,
                    response.StatusCode);
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("web", out var web) ||
                !web.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return results.EnumerateArray()
                .Select(x => new WebSearchResult
                {
                    Title = GetString(x, "title"),
                    Url = GetString(x, "url"),
                    Description = GetString(x, "description"),
                    Age = GetString(x, "age"),
                    PageAge = GetString(x, "page_age"),
                    Language = GetString(x, "language")
                })
                .ToArray();
        }

        private static void AddIfNotEmpty(IDictionary<string, string> values, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values[key] = value.Trim();
        }

        private static string GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
    }
}
