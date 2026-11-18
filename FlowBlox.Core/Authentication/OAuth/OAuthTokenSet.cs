namespace FlowBlox.Core.Authentication.OAuth;

/// <summary>
/// Local credential payload, stored only through the protected token store, never in project configuration.
/// </summary>
public sealed class OAuthTokenSet
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
}
