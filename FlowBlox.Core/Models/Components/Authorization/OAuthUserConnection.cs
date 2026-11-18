using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Authentication.OAuth;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Interfaces;
using System.Collections.Concurrent;

namespace FlowBlox.Core.Models.Components.Authorization;

[Display(Name = "OAuthUserConnection_DisplayName", ResourceType = typeof(FlowBloxTexts))]
[PluralDisplayName("OAuthUserConnection_DisplayName_Plural", typeof(FlowBloxTexts))]
public sealed class OAuthUserConnection : ManagedObject
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SignInGates = new();
    public Guid ConnectionId { get; set; } = Guid.NewGuid();

    [Required]
    [Display(Name = "OAuthUserConnection_AuthorizationEndpoint", Description = "OAuthUserConnection_AuthorizationEndpoint_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OAuthUserConnection_TokenEndpoint", Description = "OAuthUserConnection_TokenEndpoint_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    public string TokenEndpoint { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OAuthUserConnection_ClientId", Description = "OAuthUserConnection_ClientId_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    public string ClientId { get; set; } = string.Empty;

    [Display(Name = "OAuthUserConnection_ClientSecretReference", Description = "OAuthUserConnection_ClientSecretReference_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    public string ClientSecretReference { get; set; } = string.Empty;

    public string GetClientSecret() => this.Resolve(owner => owner.ClientSecretReference);

    [Display(Name = "OAuthUserConnection_Scopes", Description = "OAuthUserConnection_Scopes_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    public string Scopes { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OAuthUserConnection_ResourceBaseUrl", Description = "OAuthUserConnection_ResourceBaseUrl_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
    public string ResourceBaseUrl { get; set; } = string.Empty;

    [Range(0, 65535)]
    [Display(Name = "OAuthUserConnection_RedirectPort", Description = "OAuthUserConnection_RedirectPort_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
    public int RedirectPort { get; set; }

    [Display(Name = "OAuthUserConnection_AuthorizationParameters", Description = "OAuthUserConnection_AuthorizationParameters_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
    [FlowBloxDataGrid]
    public ObservableCollection<OAuthAuthorizationParameter> AuthorizationParameters { get; set; } = new();

    [Display(Name = "OAuthUserConnection_IsConnected", Description = "OAuthUserConnection_IsConnected_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 7)]
    [Newtonsoft.Json.JsonIgnore]
    [FlowBlox.Core.Util.DeepCopier.DeepCopierIgnore]
    [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
    public bool IsConnected
    {
        get
        {
            try
            {
                var tokens = new OAuthTokenStore().Read(GetSettings().StorageKey);
                return tokens != null && (!string.IsNullOrEmpty(tokens.RefreshToken) || tokens.ExpiresUtc > DateTime.UtcNow);
            }
            catch (Exception exception) when (exception is ValidationException or IOException or System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException)
            {
                return false;
            }
        }
    }

    public OAuthConnectionSettings GetSettings()
    {
        var settings = new OAuthConnectionSettings
        {
            ConnectionId = ConnectionId,
            AuthorizationEndpoint = this.Resolve(owner => owner.AuthorizationEndpoint).Trim(),
            TokenEndpoint = this.Resolve(owner => owner.TokenEndpoint).Trim(),
            ClientId = this.Resolve(owner => owner.ClientId).Trim(),
            ClientSecretReference = ClientSecretReference ?? string.Empty,
            Scopes = this.Resolve(owner => owner.Scopes).Trim(),
            ResourceBaseUrl = this.Resolve(owner => owner.ResourceBaseUrl).Trim(),
            RedirectPort = this.Resolve(owner => owner.RedirectPort)
        };
        foreach (var parameter in AuthorizationParameters)
        {
            if (!settings.AuthorizationParameters.TryAdd(parameter.Name?.Trim() ?? string.Empty, parameter.Resolve(owner => owner.Value)))
                throw new ValidationException("Additional OAuth authorization parameter names must be unique.");
        }
        settings.Validate();
        return settings;
    }

    public async Task<string> GetAccessTokenAsync(Uri resource, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings();
        settings.ClientSecret = GetClientSecret();
        var service = new OAuthTokenService();
        try
        {
            return await service.GetAccessTokenAsync(settings, resource, cancellationToken).ConfigureAwait(false);
        }
        catch (OAuthInteractionRequiredException)
        {
            var authorization = FlowBloxServiceLocator.Instance.GetService<IOAuthUserAuthorizationService>();
            if (authorization == null)
                throw;

            var gate = SignInGates.GetOrAdd(settings.StorageKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                try
                {
                    return await service.GetAccessTokenAsync(settings, resource, cancellationToken).ConfigureAwait(false);
                }
                catch (OAuthInteractionRequiredException)
                {
                    if (!await authorization.AuthorizeAsync(settings, cancellationToken).ConfigureAwait(false))
                        throw new OAuthInteractionRequiredException();
                }
                return await service.GetAccessTokenAsync(settings, resource, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var store = new OAuthTokenStore();
        var key = GetSettings().StorageKey;
        using var lease = await store.AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        store.Delete(key);
    }

    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.account_key_outline, 16, SKColors.SteelBlue);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.account_key_outline, 32, SKColors.SteelBlue);
}
