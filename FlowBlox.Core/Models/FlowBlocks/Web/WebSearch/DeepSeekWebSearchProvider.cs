using FlowBlox.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Fields;

namespace FlowBlox.Core.Models.FlowBlocks.Web.WebSearch
{
    [Display(Name = "DeepSeekWebSearchProvider_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("DeepSeekWebSearchProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class DeepSeekWebSearchProvider : WebSearchProviderBase
    {
        internal override bool SupportsSearchLanguage => true;

        public DeepSeekWebSearchProvider()
        {
            BaseUrl = "https://api.deepseek.com/v1/tools/search";
        }

        protected override async Task<IReadOnlyList<WebSearchResult>> SearchCoreAsync(
            WebSearchRequest request,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                query = AddLanguageFilter(request.Query, request.SearchLanguage),
                count = Math.Clamp(request.MaxResults, 1, 20),
                need_content = false
            });

            var timeoutSeconds = this.Resolve(provider => provider.TimeoutSeconds);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
            using var message = new HttpRequestMessage(HttpMethod.Post, ResolveBaseUrl());
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ResolveApiKey());
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"DeepSeek Search request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    inner: null,
                    response.StatusCode);
            }

            return ParseResponse(json);
        }

        internal static IReadOnlyList<WebSearchResult> ParseResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return results.EnumerateArray()
                .Select(x => new WebSearchResult
                {
                    Title = GetString(x, "title"),
                    Url = GetString(x, "url"),
                    Description = GetString(x, "snippet"),
                    PageAge = GetString(x, "date")
                })
                .ToArray();
        }

        internal static string AddLanguageFilter(string query, string? searchLanguage)
        {
            var language = searchLanguage?.Trim();
            return string.IsNullOrWhiteSpace(language)
                ? query
                : $"{query} lang:{language}";
        }

        private static string GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
    }
}
