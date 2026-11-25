using FlowBlox.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

/// <summary>
/// Preserves raw provider usage fields that compatibility SDKs may discard.
/// </summary>
internal sealed class AIUsageCaptureHandler : DelegatingHandler
{
    private readonly bool _anthropic;
    public AIUsage Usage { get; private set; } = new();

    public AIUsageCaptureHandler(bool anthropic) : base(new HttpClientHandler())
    {
        _anthropic = anthropic;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content != null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Usage = AIUsage.FromResponse(JObject.Parse(body), _anthropic);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidCastException)
            {
                FlowBloxLogManager.Instance.GetLogger().Warn("Failed to read AI provider usage metadata; measurements are unavailable.", ex);
            }
        }
        return response;
    }
}
