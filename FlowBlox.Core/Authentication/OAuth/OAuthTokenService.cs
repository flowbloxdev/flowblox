using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FlowBlox.Core.Authentication.OAuth;

public sealed class OAuthTokenService
{
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };
    private readonly OAuthTokenStore _store;
    private readonly HttpClient _client;

    public OAuthTokenService(OAuthTokenStore? store = null, HttpClient? client = null)
    {
        _store = store ?? new OAuthTokenStore();
        _client = client ?? DefaultClient;
    }

    public async Task<string> GetAccessTokenAsync(OAuthConnectionSettings settings, Uri resource, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        settings.ValidateResource(resource);
        using var lease = await _store.AcquireAsync(settings.StorageKey, cancellationToken).ConfigureAwait(false);
        OAuthTokenSet? tokens;
        try
        {
            tokens = _store.Read(settings.StorageKey);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException or JsonException)
        {
            throw new OAuthInteractionRequiredException();
        }
        if (tokens == null)
            throw new OAuthInteractionRequiredException();
        if (tokens.ExpiresUtc > DateTime.UtcNow.AddSeconds(30) && !string.IsNullOrEmpty(tokens.AccessToken))
            return tokens.AccessToken;
        if (string.IsNullOrEmpty(tokens.RefreshToken))
            throw new OAuthInteractionRequiredException();

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = settings.ClientId,
            ["refresh_token"] = tokens.RefreshToken
        };
        var clientSecret = string.IsNullOrEmpty(settings.ClientSecret) ? tokens.ClientSecret : settings.ClientSecret;
        AddClientSecret(parameters, clientSecret);
        tokens.ClientSecret = clientSecret;
        OAuthTokenSet updated;
        try
        {
            updated = await RequestTokensAsync(settings, parameters, tokens, cancellationToken).ConfigureAwait(false);
        }
        catch (OAuthInteractionRequiredException)
        {
            _store.Delete(settings.StorageKey);
            throw;
        }
        _store.Write(settings.StorageKey, updated);
        return updated.AccessToken;
    }

    public async Task CompleteAuthorizationAsync(
        OAuthConnectionSettings settings,
        string code,
        string verifier,
        string redirectUri,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = settings.ClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri
        };
        AddClientSecret(parameters, clientSecret);
        var tokens = await RequestTokensAsync(settings, parameters, new OAuthTokenSet { ClientSecret = clientSecret }, cancellationToken)
            .ConfigureAwait(false);
        using var lease = await _store.AcquireAsync(settings.StorageKey, cancellationToken).ConfigureAwait(false);
        _store.Write(settings.StorageKey, tokens);
    }

    private async Task<OAuthTokenSet> RequestTokensAsync(
        OAuthConnectionSettings settings,
        Dictionary<string, string> parameters,
        OAuthTokenSet previous,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExchangeAsync(settings, parameters, previous, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new ValidationException("The OAuth token endpoint returned an invalid response.");
        }
        catch (HttpRequestException)
        {
            throw new ValidationException("The OAuth token endpoint could not be reached.");
        }
    }

    private async Task<OAuthTokenSet> ExchangeAsync(
        OAuthConnectionSettings settings,
        Dictionary<string, string> parameters,
        OAuthTokenSet previous,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _client.PostAsync(settings.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        // Never include the token response, authorization code or client secret in exception/log messages.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = json.RootElement;
        if (!response.IsSuccessStatusCode)
        {
            if (root.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant")
                throw new OAuthInteractionRequiredException();
            throw new ValidationException($"OAuth token exchange failed (HTTP {(int)response.StatusCode}).");
        }
        if (!root.TryGetProperty("access_token", out var access) || string.IsNullOrWhiteSpace(access.GetString())
            || !root.TryGetProperty("token_type", out var type) || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("The OAuth provider did not return a valid Bearer access token.");

        var expires = 300;
        if (root.TryGetProperty("expires_in", out var lifetime) && !int.TryParse(lifetime.ToString(), out expires))
            throw new ValidationException("The OAuth provider returned an invalid access-token lifetime.");
        if (expires <= 0 || expires > 31536000)
            throw new ValidationException("The OAuth provider returned an invalid access-token lifetime.");
        return new OAuthTokenSet
        {
            AccessToken = access.GetString()!,
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh) && !string.IsNullOrEmpty(refresh.GetString())
                ? refresh.GetString()!
                : previous.RefreshToken,
            ClientSecret = previous.ClientSecret,
            ExpiresUtc = DateTime.UtcNow.AddSeconds(expires)
        };
    }

    private static void AddClientSecret(Dictionary<string, string> parameters, string secret)
    {
        if (!string.IsNullOrEmpty(secret))
            parameters["client_secret"] = secret;
    }
}
