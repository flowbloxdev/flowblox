using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;

namespace FlowBlox.Core.Models.FlowBlocks.Authorization
{
    [Display(Name = "OAuthFlowBlock_DisplayName", Description = "OAuthFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class OAuthFlowBlock : BaseSingleResultFlowBlock
    {
        [Required]
        [Display(Name = "OAuthFlowBlock_TokenEndpoint", Description = "OAuthFlowBlock_TokenEndpoint_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string TokenEndpoint { get; set; }

        [Required]
        [Display(Name = "OAuthFlowBlock_ClientId", Description = "OAuthFlowBlock_ClientId_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ClientId { get; set; }

        [Required]
        [Display(Name = "OAuthFlowBlock_ClientSecret", Description = "OAuthFlowBlock_ClientSecret_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ClientSecret { get; set; }

        [Display(Name = "OAuthFlowBlock_Scope", Description = "OAuthFlowBlock_Scope_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Scope { get; set; }

        [Display(Name = "OAuthFlowBlock_Audience", Description = "OAuthFlowBlock_Audience_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Audience { get; set; }

        [Display(Name = "OAuthFlowBlock_GrantType", Description = "OAuthFlowBlock_GrantType_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public OAuthGrantType GrantType { get; set; } = OAuthGrantType.ClientCredentials;

        [Display(Name = "OAuthFlowBlock_SendClientCredentialsInBody", Description = "OAuthFlowBlock_SendClientCredentialsInBody_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool SendClientCredentialsInBody { get; set; } = true;

        [ActivationCondition(MemberName = nameof(GrantType), Value = OAuthGrantType.Password)]
        [ConditionallyRequired]
        [Display(Name = "OAuthFlowBlock_UserName", Description = "OAuthFlowBlock_UserName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 7)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string UserName { get; set; }

        [ActivationCondition(MemberName = nameof(GrantType), Value = OAuthGrantType.Password)]
        [ConditionallyRequired]
        [Display(Name = "OAuthFlowBlock_Password", Description = "OAuthFlowBlock_Password_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 8)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Password { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.identifier, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.identifier, 32, SKColors.SteelBlue);

        public override FieldTypes DefaultResultFieldType => FieldTypes.Text;

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Authorization;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(TokenEndpoint));
            properties.Add(nameof(ClientId));
            properties.Add(nameof(Scope));
            properties.Add(nameof(Audience));
            properties.Add(nameof(GrantType));
            if (GrantType == OAuthGrantType.Password)
                properties.Add(nameof(UserName));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var tokenEndpoint = this.Resolve(owner => owner.TokenEndpoint).Trim();
                var clientId = this.Resolve(owner => owner.ClientId).Trim();
                var clientSecret = this.Resolve(owner => owner.ClientSecret);
                var scope = this.Resolve(owner => owner.Scope).Trim();
                var audience = this.Resolve(owner => owner.Audience).Trim();
                var userName = this.Resolve(owner => owner.UserName).Trim();
                var password = this.Resolve(owner => owner.Password);

                if (string.IsNullOrWhiteSpace(tokenEndpoint))
                    throw new ValidationException("OAuth token endpoint is empty.");

                if (string.IsNullOrWhiteSpace(clientId))
                    throw new ValidationException("OAuth client id is empty.");

                if (GrantType == OAuthGrantType.Password && string.IsNullOrWhiteSpace(userName))
                    throw new ValidationException("OAuth user name is empty for the password grant.");
                if (GrantType == OAuthGrantType.Password && string.IsNullOrEmpty(password))
                    throw new ValidationException("OAuth password is empty for the password grant.");

                string token;
                try
                {
                    token = RequestToken(tokenEndpoint, clientId, clientSecret, scope,
                        audience, GrantType, userName, password).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    runtime.Report(ex.ToString());
                    CreateNotification(runtime, OAuthNotifications.AuthorizationFailed);
                    throw;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    CreateNotification(runtime, OAuthNotifications.AuthorizationFailed);
                    throw new InvalidOperationException("OAuth authorization failed. Access token is empty.");
                }

                GenerateResult(runtime, [token]);
            });
        }

        private async Task<string> RequestToken(
            string tokenEndpoint,
            string clientId,
            string clientSecret,
            string scope,
            string audience,
            OAuthGrantType grantType,
            string userName,
            string password)
        {
            using var httpClient = new HttpClient();

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = grantType == OAuthGrantType.Password ? "password" : "client_credentials"
            };

            if (grantType == OAuthGrantType.Password)
            {
                form["username"] = userName ?? string.Empty;
                form["password"] = password ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(scope))
                form["scope"] = scope;

            if (!string.IsNullOrWhiteSpace(audience))
                form["audience"] = audience;

            if (SendClientCredentialsInBody)
            {
                form["client_id"] = clientId;
                form["client_secret"] = clientSecret ?? string.Empty;
            }
            else
            {
                var basicAuth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            }

            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(tokenEndpoint, content).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"OAuth token request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {payload}");

            if (string.IsNullOrWhiteSpace(payload))
                throw new InvalidOperationException("OAuth token request returned an empty response.");

            var json = JObject.Parse(payload);
            var accessToken = json["access_token"]?.Value<string>();
            return accessToken ?? string.Empty;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(OAuthNotifications));
                return notificationTypes;
            }
        }

        public enum OAuthNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "OAuthFlowBlock_Notification_AuthorizationFailed")]
            AuthorizationFailed
        }
    }
}
