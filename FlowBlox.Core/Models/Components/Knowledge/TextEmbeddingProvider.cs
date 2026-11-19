using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Knowledge;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace FlowBlox.Core.Models.Components.Knowledge;

[Display(Name = "TextEmbeddingProvider_DisplayName", Description = "TextEmbeddingProvider_Description", ResourceType = typeof(FlowBloxTexts))]
public class TextEmbeddingProvider : TextEmbeddingProviderBase
{
    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.vector_polyline, 16, SKColors.Teal);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.vector_polyline, 32, SKColors.Teal);

    [Required]
    [Display(Name = "TextEmbeddingProvider_Endpoint", Description = "TextEmbeddingProvider_Endpoint_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    public string Endpoint { get; set; } = "https://api.openai.com/v1/embeddings";

    [Required]
    [Display(Name = "AIProvider_ApiKey", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    public string ApiKey { get; set; }

    [Required]
    [Display(Name = "AIProvider_DefaultModel", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    public string Model { get; set; } = "text-embedding-3-small";

    [Range(1, 3600)]
    [Display(Name = "AIProvider_TimeoutSeconds", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    public int TimeoutSeconds { get; set; } = 120;

    public override string GetProfile() => this.Resolve(provider => provider.Endpoint, required: true).TrimEnd('/')
        + "|" + this.Resolve(provider => provider.Model, required: true);

    public override List<string> GetDisplayableProperties() => [nameof(Name), nameof(Endpoint), nameof(Model)];

    public override async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var endpoint = this.Resolve(provider => provider.Endpoint, required: true);
        var apiKey = this.Resolve(provider => provider.ApiKey, required: true);
        var model = this.Resolve(provider => provider.Model, required: true);
        using var client = CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(this.Resolve(provider => provider.TimeoutSeconds));
        var vectors = new List<float[]>();
        foreach (var batch in texts.Chunk(32))
        {
            if (batch.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Empty text cannot be embedded.");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                model,
                input = batch,
                encoding_format = "float"
            });
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var data = json["data"] as JArray
                ?? throw new InvalidDataException("The embedding response contains no data array.");
            var ordered = data.OrderBy(item => item.Value<int>("index")).ToArray();
            if (ordered.Length != batch.Length || !ordered.Select(item => item.Value<int>("index")).SequenceEqual(Enumerable.Range(0, batch.Length)))
                throw new InvalidDataException("The embedding response does not contain exactly one vector for every input.");

            foreach (var item in ordered)
            {
                var vector = item["embedding"]?.ToObject<float[]>()
                    ?? throw new InvalidDataException("An embedding vector is missing.");
                VectorMath.Validate(vector);
                if (vectors.Count > 0 && vectors[0].Length != vector.Length)
                    throw new InvalidDataException("The embedding response contains inconsistent vector dimensions.");

                vectors.Add(vector);
            }
        }

        return vectors;
    }

    protected virtual HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false });
}
