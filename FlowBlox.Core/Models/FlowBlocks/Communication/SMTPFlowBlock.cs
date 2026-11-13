using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Services.Communication;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace FlowBlox.Core.Models.FlowBlocks.Communication
{
    [Display(Name = "SMTPFlowBlock_DisplayName", Description = "SMTPFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class SMTPFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Required]
        [Display(Name = "SMTPFlowBlock_Host", Description = "SMTPFlowBlock_Host_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public string Host { get; set; }

        [Display(Name = "SMTPFlowBlock_Port", Description = "SMTPFlowBlock_Port_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public int Port { get; set; } = 25;

        [Display(Name = "SMTPFlowBlock_UseSsl", Description = "SMTPFlowBlock_UseSsl_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public bool UseSsl { get; set; }

        [Display(Name = "SMTPFlowBlock_AcceptInvalidCertificates", Description = "SMTPFlowBlock_AcceptInvalidCertificates_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public bool AcceptInvalidCertificates { get; set; } = true;

        [Display(Name = "SMTPFlowBlock_UseAuthentication", Description = "SMTPFlowBlock_UseAuthentication_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public bool UseAuthentication { get; set; }

        [Display(Name = "SMTPFlowBlock_UserName", Description = "SMTPFlowBlock_UserName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        public string UserName { get; set; }

        [Display(Name = "SMTPFlowBlock_Password", Description = "SMTPFlowBlock_Password_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxTextBox]
        public string Password { get; set; }

        [Required]
        [Display(Name = "SMTPFlowBlock_FromAddress", Description = "SMTPFlowBlock_FromAddress_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        public string FromAddress { get; set; }

        [Required]
        [Display(Name = "SMTPFlowBlock_ToAddresses", Description = "SMTPFlowBlock_ToAddresses_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 7)]
        public string ToAddresses { get; set; }

        [Display(Name = "SMTPFlowBlock_CcAddresses", Description = "SMTPFlowBlock_CcAddresses_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 8)]
        public string CcAddresses { get; set; }

        [Display(Name = "SMTPFlowBlock_BccAddresses", Description = "SMTPFlowBlock_BccAddresses_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 9)]
        public string BccAddresses { get; set; }

        [Display(Name = "SMTPFlowBlock_Subject", Description = "SMTPFlowBlock_Subject_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 10)]
        public string Subject { get; set; }

        [Display(Name = "SMTPFlowBlock_Body", Description = "SMTPFlowBlock_Body_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 11)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true)]
        public string Body { get; set; }

        [Display(Name = "SMTPFlowBlock_IsBodyHtml", Description = "SMTPFlowBlock_IsBodyHtml_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 12)]
        public bool IsBodyHtml { get; set; }

        [Display(Name = "SMTPFlowBlock_Attachments", Description = "SMTPFlowBlock_Attachments_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 13)]
        [FlowBloxDataGrid]
        public ObservableCollection<SmtpAttachmentMappingEntry> Attachments { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.email_send_outline, 16, SKColors.CadetBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.email_send_outline, 32, SKColors.CadetBlue);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Communication;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Host));
            properties.Add(nameof(Port));
            properties.Add(nameof(UseSsl));
            properties.Add(nameof(AcceptInvalidCertificates));
            properties.Add(nameof(UseAuthentication));
            properties.Add(nameof(FromAddress));
            properties.Add(nameof(ToAddresses));
            properties.Add(nameof(Subject));
            properties.Add(nameof(IsBodyHtml));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var port = this.Resolve(x => x.Port);
                var useSsl = this.Resolve(x => x.UseSsl);

                var host = this.Resolve(owner => owner.Host).Trim();
                var fromAddress = this.Resolve(owner => owner.FromAddress).Trim();
                var toAddresses = this.Resolve(owner => owner.ToAddresses);
                var ccAddresses = this.Resolve(owner => owner.CcAddresses);
                var bccAddresses = this.Resolve(owner => owner.BccAddresses);
                var subject = this.Resolve(owner => owner.Subject);
                var body = this.Resolve(owner => owner.Body);

                if (string.IsNullOrWhiteSpace(host))
                {
                    CreateNotification(runtime, SMTPNotifications.HostIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (string.IsNullOrWhiteSpace(fromAddress))
                {
                    CreateNotification(runtime, SMTPNotifications.FromAddressIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (SmtpMailSender.ParseAddresses(toAddresses).Count == 0)
                {
                    CreateNotification(runtime, SMTPNotifications.ToAddressesAreEmpty);
                    GenerateResult(runtime);
                    return;
                }
                var sendSucceeded = true;
                try
                {
                    var userName = this.Resolve(owner => owner.UserName);
                    var password = this.Resolve(owner => owner.Password);
                    var sender = new SmtpMailSender();
                    sender.Send(new SmtpMailSettings
                    {
                        Host = host,
                        Port = port,
                        UseSsl = useSsl,
                        AcceptInvalidCertificates = this.Resolve(x => x.AcceptInvalidCertificates),
                        UseAuthentication = this.Resolve(x => x.UseAuthentication),
                        UserName = userName ?? string.Empty,
                        Password = password ?? string.Empty
                    }, new SmtpMailRequest
                    {
                        FromAddress = fromAddress,
                        ToAddresses = toAddresses,
                        CcAddresses = ccAddresses,
                        BccAddresses = bccAddresses,
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = this.Resolve(x => x.IsBodyHtml),
                        Attachments = BuildAttachments()
                    });
                    runtime.Report($"SMTP mail sent successfully via '{host}:{port}'.");
                }
                catch (Exception ex)
                {
                    runtime.Report(ex.ToString());
                    CreateNotification(runtime, SMTPNotifications.MailSendFailure);
                    sendSucceeded = false;
                }

                if (sendSucceeded)
                    GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
                else
                    GenerateResult(runtime);
            });
        }

        private List<SmtpMailAttachment> BuildAttachments()
        {
            var attachments = new List<SmtpMailAttachment>();
            foreach (var mapping in Attachments ?? Enumerable.Empty<SmtpAttachmentMappingEntry>())
            {
                if (mapping == null || mapping.Field == null)
                    continue;

                var fileName = mapping.Resolve(owner => owner.FileName).Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                var bytes = ConvertFieldToBytes(mapping);
                attachments.Add(new SmtpMailAttachment
                {
                    FileName = fileName,
                    MediaType = MediaTypeNames.Application.Octet,
                    Content = bytes
                });
            }

            return attachments;
        }

        private static byte[] ConvertFieldToBytes(SmtpAttachmentMappingEntry mapping)
        {
            var configuredType = mapping.Field.GetConfiguredType();
            var value = mapping.Field.Value;

            if (configuredType == typeof(byte[]))
                return value as byte[] ?? [];

            if (configuredType == typeof(string))
            {
                var encoding = mapping.Resolve(entry => entry.EncodingName).ToEncoding();
                return encoding.GetBytes(value?.ToString() ?? string.Empty);
            }

            var fallback = value?.ToString() ?? string.Empty;
            return mapping.Resolve(entry => entry.EncodingName).ToEncoding().GetBytes(fallback);
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(SMTPNotifications));
                return notificationTypes;
            }
        }

        public enum SMTPNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "SMTP host is empty")]
            HostIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "From address is empty")]
            FromAddressIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "No To addresses configured")]
            ToAddressesAreEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Mail send failure")]
            MailSendFailure
        }
    }
}
