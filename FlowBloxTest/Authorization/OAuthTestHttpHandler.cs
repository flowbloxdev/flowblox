namespace FlowBloxTest.Authorization;

internal sealed class OAuthTestHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send { get; set; } = null!;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Send(request, cancellationToken);
}
