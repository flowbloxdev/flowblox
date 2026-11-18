using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Authentication.OAuth;

/// <summary>
/// Owns a PKCE exchange and a loopback-only callback listener. It never opens a browser itself.
/// </summary>
public sealed class OAuthAuthorizationSession : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _state = Encode(RandomNumberGenerator.GetBytes(32));
    public string CodeVerifier { get; } = Encode(RandomNumberGenerator.GetBytes(32));
    public string RedirectUri { get; }
    public Uri AuthorizationUri { get; }

    public OAuthAuthorizationSession(OAuthConnectionSettings settings)
    {
        settings.Validate();
        _listener = new TcpListener(IPAddress.Loopback, settings.RedirectPort);
        _listener.ExclusiveAddressUse = true;
        _listener.Start();
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/oauth/callback/";
        var parameters = new Dictionary<string, string>(settings.AuthorizationParameters)
        {
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = settings.Scopes,
            ["state"] = _state,
            ["code_challenge"] = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(CodeVerifier))),
            ["code_challenge_method"] = "S256"
        };
        AuthorizationUri = new Uri(settings.AuthorizationEndpoint + "?" + string.Join("&",
            parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    }

    public async Task<string> WaitForCodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var code = await ReadCallbackAsync(client, timeout.Token).ConfigureAwait(false);
                if (code != null)
                    return code;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // An unrelated or incomplete loopback request must not consume the authorization session.
            }
            catch (IOException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private async Task<string?> ReadCallbackAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var parts = requestLine?.Split(' ');
        if (parts == null || parts.Length != 3 || parts[0] != "GET" || parts[1].Length > 8192 || !parts[1].StartsWith('/'))
            return null;
        var callback = new Uri(new Uri(RedirectUri), parts[1]);
        var headerLength = 0;
        string? header;
        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)))
        {
            headerLength += header.Length;
            if (headerLength > 16384)
                return null;
        }
        var query = ParseQuery(callback.Query);
        var validState = query.TryGetValue("state", out var state)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_state), Encoding.UTF8.GetBytes(state));
        if (callback.Authority != new Uri(RedirectUri).Authority || callback.AbsolutePath != "/oauth/callback/" || !validState)
        {
            await WriteResponseAsync(stream, false, cancellationToken).ConfigureAwait(false);
            return null;
        }
        await WriteResponseAsync(stream, true, cancellationToken).ConfigureAwait(false);
        if (query.ContainsKey("error"))
            throw new ValidationException("The OAuth authorization was declined or cancelled by the provider.");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new ValidationException("The OAuth callback did not include an authorization code.");
        return code;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(parts[0]);
            if (!result.TryAdd(key, parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty))
                return new Dictionary<string, string>();
        }
        return result;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, bool accepted, CancellationToken cancellationToken)
    {
        var message = FlowBloxResourceUtil.GetLocalizedString(
            accepted ? "OAuthAuthorizationSession_BrowserComplete" : "OAuthAuthorizationSession_BrowserRejected",
            typeof(FlowBloxTexts));
        var body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=\"utf-8\"><title>FlowBlox</title><p>"
            + WebUtility.HtmlEncode(message) + "</p>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(accepted ? "200 OK" : "400 Bad Request")}\r\n"
            + $"Content-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() => _listener.Stop();
}
