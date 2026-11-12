using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Renci.SshNet;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace FlowBlox.Core.Models.Components.IO
{
    [Display(Name = "SftpConnectionProvider_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("SftpConnectionProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class SftpConnectionProvider : ManagedObject
    {
        [Required]
        [Display(Name = "SftpConnectionProvider_Host", Description = "SftpConnectionProvider_Host_Tooltip", GroupName = "SftpConnectionProvider_Groups_Connection", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Host { get; set; }

        [Display(Name = "SftpConnectionProvider_Port", Description = "SftpConnectionProvider_Port_Tooltip", GroupName = "SftpConnectionProvider_Groups_Connection", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int Port { get; set; } = 22;

        [Display(Name = "SftpConnectionProvider_AuthenticationMethod", Description = "SftpConnectionProvider_AuthenticationMethod_Tooltip", GroupName = "SftpConnectionProvider_Groups_Authentication", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public SftpAuthenticationMethod AuthenticationMethod { get; set; } = SftpAuthenticationMethod.Password;

        [Required]
        [Display(Name = "SftpConnectionProvider_UserName", Description = "SftpConnectionProvider_UserName_Tooltip", GroupName = "SftpConnectionProvider_Groups_Authentication", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string UserName { get; set; }

        [ActivationCondition(MemberName = nameof(AuthenticationMethod), Values = [SftpAuthenticationMethod.Password, SftpAuthenticationMethod.PasswordAndPrivateKey], IsRequired = true)]
        [Display(Name = "SftpConnectionProvider_Password", Description = "SftpConnectionProvider_Password_Tooltip", GroupName = "SftpConnectionProvider_Groups_PasswordAuthentication", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Password { get; set; }

        [ActivationCondition(MemberName = nameof(AuthenticationMethod), Values = [SftpAuthenticationMethod.PrivateKey, SftpAuthenticationMethod.PasswordAndPrivateKey], IsRequired = true)]
        [Display(Name = "SftpConnectionProvider_PrivateKey", Description = "SftpConnectionProvider_PrivateKey_Tooltip", GroupName = "SftpConnectionProvider_Groups_PrivateKeyAuthentication", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFileSelection)]
        [FlowBloxTextBox(MultiLine = true)]
        public string PrivateKey { get; set; }

        [ActivationCondition(MemberName = nameof(AuthenticationMethod), Values = [SftpAuthenticationMethod.PrivateKey, SftpAuthenticationMethod.PasswordAndPrivateKey])]
        [Display(Name = "SftpConnectionProvider_PrivateKeyPassphrase", Description = "SftpConnectionProvider_PrivateKeyPassphrase_Tooltip", GroupName = "SftpConnectionProvider_Groups_PrivateKeyAuthentication", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string PrivateKeyPassphrase { get; set; }

        [Display(Name = "SftpConnectionProvider_HostKeyFingerprint", Description = "SftpConnectionProvider_HostKeyFingerprint_Tooltip", GroupName = "SftpConnectionProvider_Groups_HostKeyVerification", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string HostKeyFingerprint { get; set; }

        [Display(Name = "SftpConnectionProvider_TrustAnyHostKey", Description = "SftpConnectionProvider_TrustAnyHostKey_Tooltip", GroupName = "SftpConnectionProvider_Groups_HostKeyVerification", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool TrustAnyHostKey { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cube_outline, 16, SKColors.Teal);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cube_outline, 32, SKColors.Teal);

        public SftpClient CreateClient()
        {
            var host = this.Resolve(owner => owner.Host).Trim();
            var userName = this.Resolve(owner => owner.UserName).Trim();
            var authenticationMethod = this.Resolve(provider => provider.AuthenticationMethod);
            var port = this.Resolve(provider => provider.Port);
            var trustAnyHostKey = this.Resolve(provider => provider.TrustAnyHostKey);
            var usePassword = authenticationMethod is SftpAuthenticationMethod.Password or SftpAuthenticationMethod.PasswordAndPrivateKey;
            var usePrivateKey = authenticationMethod is SftpAuthenticationMethod.PrivateKey or SftpAuthenticationMethod.PasswordAndPrivateKey;
            var password = usePassword ? this.Resolve(owner => owner.Password) : string.Empty;
            var keySource = usePrivateKey ? this.Resolve(owner => owner.PrivateKey) : string.Empty;
            var passphrase = usePrivateKey ? this.Resolve(owner => owner.PrivateKeyPassphrase) : string.Empty;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(userName))
                throw new ValidationException("SFTP host and user name are required.");
            if (usePassword && string.IsNullOrEmpty(password))
                throw new ValidationException("An SFTP password is required for the selected authentication method.");
            if (usePrivateKey && string.IsNullOrWhiteSpace(keySource))
                throw new ValidationException("An SFTP private key is required for the selected authentication method.");

            var methods = new List<AuthenticationMethod>();
            if (usePassword)
                methods.Add(new PasswordAuthenticationMethod(userName, password));

            if (usePrivateKey)
            {
                var keyBytes = ResolvePrivateKey(keySource);
                var keyStream = new MemoryStream(keyBytes, writable: false);
                var keyFile = string.IsNullOrEmpty(passphrase)
                    ? new PrivateKeyFile(keyStream)
                    : new PrivateKeyFile(keyStream, passphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(userName, keyFile));
            }

            var client = new SftpClient(new ConnectionInfo(host, port, userName, methods.ToArray()));
            var expectedFingerprint = NormalizeFingerprint(
                this.Resolve(owner => owner.HostKeyFingerprint));

            client.HostKeyReceived += (_, args) =>
            {
                args.CanTrust = trustAnyHostKey ||
                    (!string.IsNullOrWhiteSpace(expectedFingerprint) &&
                     string.Equals(NormalizeFingerprint(args.FingerPrintSHA256), expectedFingerprint, StringComparison.Ordinal));
            };

            return client;
        }

        private static byte[] ResolvePrivateKey(string source)
        {
            var candidate = source.Trim();
            if (File.Exists(candidate))
                return File.ReadAllBytes(candidate);
            if (candidate.Contains("-----BEGIN", StringComparison.Ordinal))
                return Encoding.UTF8.GetBytes(source);
            try { return Convert.FromBase64String(candidate); }
            catch (FormatException ex)
            {
                throw new FormatException("The SFTP private key must be PEM content, Base64 content, or an existing file path.", ex);
            }
        }

        private static string NormalizeFingerprint(string value)
            => (value ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
