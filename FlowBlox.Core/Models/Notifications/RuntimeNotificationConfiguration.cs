using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Services.Communication;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace FlowBlox.Core.Models.Notifications
{
    [Display(Name = "RuntimeNotificationConfiguration_DisplayName", Description = "RuntimeNotificationConfiguration_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUIGroup("RuntimeNotificationConfiguration_Groups_Notifications", 0)]
    [FlowBloxUIGroup("RuntimeNotificationConfiguration_Groups_Server", 10)]
    [FlowBloxUIGroup("RuntimeNotificationConfiguration_Groups_Authentication", 20)]
    [FlowBloxUIGroup("RuntimeNotificationConfiguration_Groups_Recipients", 30)]
    public sealed class RuntimeNotificationConfiguration : FlowBloxReactiveObject, IValidatableObject
    {
        [Display(Name = "RuntimeNotificationConfiguration_Notifications", Description = "RuntimeNotificationConfiguration_Notifications_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Notifications", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxDataGrid(ColumnMemberNames = [nameof(RuntimeNotificationRule.Type), nameof(RuntimeNotificationRule.Enabled)])]
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        [FlowBloxAssociation(Operations = UIOperations.None)]
        public ObservableCollection<RuntimeNotificationRule> Notifications { get; set; } = CreateDefaultRules();

        [Display(Name = "RuntimeNotificationConfiguration_DoNotSendWhileDebugging", Description = "RuntimeNotificationConfiguration_DoNotSendWhileDebugging_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Notifications", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool DoNotSendWhileDebugging { get; set; } = true;

        [Display(Name = "RuntimeNotificationConfiguration_AttachRuntimeLog", Description = "RuntimeNotificationConfiguration_AttachRuntimeLog_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Notifications", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AttachRuntimeLog { get; set; } = true;

        [Display(Name = "RuntimeNotificationConfiguration_Host", Description = "RuntimeNotificationConfiguration_Host_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Server", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [Required(ErrorMessageResourceName = "RuntimeNotificationConfiguration_Validation_HostRequired", ErrorMessageResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string Host { get; set; } = string.Empty;

        [Range(1, 65535)]
        [Display(Name = "RuntimeNotificationConfiguration_Port", Description = "RuntimeNotificationConfiguration_Port_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Server", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int Port { get; set; } = 25;

        [Display(Name = "RuntimeNotificationConfiguration_UseSsl", Description = "RuntimeNotificationConfiguration_UseSsl_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Server", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UseSsl { get; set; }

        [Display(Name = "RuntimeNotificationConfiguration_AcceptInvalidCertificates", Description = "RuntimeNotificationConfiguration_AcceptInvalidCertificates_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Server", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AcceptInvalidCertificates { get; set; }

        [Display(Name = "RuntimeNotificationConfiguration_UseAuthentication", Description = "RuntimeNotificationConfiguration_UseAuthentication_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Authentication", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool UseAuthentication { get; set; }

        [ActivationCondition(MemberName = nameof(UseAuthentication), Value = true)]
        [ConditionallyRequired]
        [Display(Name = "RuntimeNotificationConfiguration_UserName", Description = "RuntimeNotificationConfiguration_UserName_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Authentication", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string UserName { get; set; } = string.Empty;

        [JsonIgnore]
        [ActivationCondition(MemberName = nameof(UseAuthentication), Value = true)]
        [ConditionallyRequired]
        [FlowBloxTextBox(IsPassword = true)]
        [Display(Name = "RuntimeNotificationConfiguration_Password", Description = "RuntimeNotificationConfiguration_Password_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Authentication", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "RuntimeNotificationConfiguration_FromAddress", Description = "RuntimeNotificationConfiguration_FromAddress_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Recipients", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [Required(ErrorMessageResourceName = "RuntimeNotificationConfiguration_Validation_FromRequired", ErrorMessageResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string FromAddress { get; set; } = string.Empty;

        [Display(Name = "RuntimeNotificationConfiguration_ToAddresses", Description = "RuntimeNotificationConfiguration_ToAddresses_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Recipients", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [Required(ErrorMessageResourceName = "RuntimeNotificationConfiguration_Validation_ToRequired", ErrorMessageResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string ToAddresses { get; set; } = string.Empty;

        [Display(Name = "RuntimeNotificationConfiguration_CcAddresses", Description = "RuntimeNotificationConfiguration_CcAddresses_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Recipients", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string CcAddresses { get; set; } = string.Empty;

        [Display(Name = "RuntimeNotificationConfiguration_BccAddresses", Description = "RuntimeNotificationConfiguration_BccAddresses_Tooltip", GroupName = "RuntimeNotificationConfiguration_Groups_Recipients", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string BccAddresses { get; set; } = string.Empty;

        public override string ToString() => Host;

        public bool IsEnabled(RuntimeNotificationType type) =>
            Notifications?.Any(x => x != null && x.Type == type && x.Enabled) == true;

        public void EnsureDefaultRules()
        {
            Notifications ??= new ObservableCollection<RuntimeNotificationRule>();
            foreach (var type in Enum.GetValues<RuntimeNotificationType>())
            {
                if (!Notifications.Any(x => x != null && x.Type == type))
                    Notifications.Add(new RuntimeNotificationRule { Type = type });
            }
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrWhiteSpace(FromAddress) && SmtpMailSender.ParseAddresses(FromAddress).Count != 1)
                yield return Error("RuntimeNotificationConfiguration_Validation_FromRequired", nameof(FromAddress));
            if (!string.IsNullOrWhiteSpace(ToAddresses) && SmtpMailSender.ParseAddresses(ToAddresses).Count == 0)
                yield return Error("RuntimeNotificationConfiguration_Validation_ToRequired", nameof(ToAddresses));

            foreach (var propertyAndValue in new[]
            {
                (nameof(FromAddress), FromAddress),
                (nameof(ToAddresses), ToAddresses),
                (nameof(CcAddresses), CcAddresses),
                (nameof(BccAddresses), BccAddresses)
            })
            {
                foreach (var address in SmtpMailSender.ParseAddresses(propertyAndValue.Item2))
                {
                    var isValid = true;
                    try
                    {
                        _ = new MailAddress(address);
                    }
                    catch (FormatException)
                    {
                        isValid = false;
                    }

                    if (!isValid)
                    {
                        yield return new ValidationResult(
                            string.Format(FlowBloxTexts.RuntimeNotificationConfiguration_Validation_InvalidAddress, address),
                            [propertyAndValue.Item1]);
                    }
                }
            }
        }

        private static ValidationResult Error(string resourceKey, string memberName) =>
            new(Util.Resources.FlowBloxResourceUtil.GetLocalizedString(resourceKey, typeof(FlowBloxTexts)), [memberName]);

        private static ObservableCollection<RuntimeNotificationRule> CreateDefaultRules() =>
            new(Enum.GetValues<RuntimeNotificationType>().Select(x => new RuntimeNotificationRule { Type = x }));
    }
}
