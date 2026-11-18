using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace FlowBlox.Core.Authentication.OAuth;

public sealed class OAuthConnectionSettings
{
    public Guid ConnectionId { get; set; }
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretReference { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string ClientSecret { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public string ResourceBaseUrl { get; set; } = string.Empty;
    public int RedirectPort { get; set; }
    public Dictionary<string, string> AuthorizationParameters { get; set; } = new(StringComparer.Ordinal);

    public string StorageKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", ConnectionId, AuthorizationEndpoint, TokenEndpoint, ClientId, ClientSecretReference, Scopes, ResourceBaseUrl,
            string.Join("&", AuthorizationParameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)))))));

    public void Validate()
    {
        RequireSecureUri(AuthorizationEndpoint);
        var tokenEndpoint = RequireSecureUri(TokenEndpoint);
        RequireSecureUri(ResourceBaseUrl);
        if (!string.IsNullOrEmpty(tokenEndpoint.Query) || string.IsNullOrWhiteSpace(ClientId)
            || ConnectionId == Guid.Empty || RedirectPort < 0 || RedirectPort > 65535)
            throw new ValidationException("OAuth client ID, token endpoint or redirect port is invalid.");

        if (!string.IsNullOrEmpty(new Uri(AuthorizationEndpoint).Query))
            throw new ValidationException("Configure authorization endpoint query values as additional authorization parameters.");

        foreach (var parameter in AuthorizationParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key) || ReservedParameters.Contains(parameter.Key))
                throw new ValidationException("An additional OAuth parameter conflicts with the authorization-code flow.");
        }
    }

    public void ValidateResource(Uri resource)
    {
        var allowed = RequireSecureUri(ResourceBaseUrl);
        RequireSecureUri(resource.AbsoluteUri);
        var prefix = allowed.AbsolutePath.TrimEnd('/');
        if (!string.Equals(resource.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(resource.IdnHost, allowed.IdnHost, StringComparison.OrdinalIgnoreCase)
            || resource.Port != allowed.Port
            || !(resource.AbsolutePath == prefix || resource.AbsolutePath.StartsWith(prefix + "/", StringComparison.Ordinal)))
            throw new ValidationException("The OAuth connection is not authorized for this API URL.");
    }

    private static readonly HashSet<string> ReservedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "client_id",
        "redirect_uri",
        "response_type",
        "scope",
        "state",
        "code_challenge",
        "code_challenge_method"
    };

    private static Uri RequireSecureUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ValidationException("OAuth endpoints and API URLs must use HTTPS (HTTP is allowed only on loopback).");
        return uri;
    }
}
