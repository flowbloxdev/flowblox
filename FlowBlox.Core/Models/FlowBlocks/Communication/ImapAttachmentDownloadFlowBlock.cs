using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Communication;
using FlowBlox.Core.Util.Resources;
using MailKit;
using MailKit.Net.Imap;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FlowBlox.Core.Models.FlowBlocks.Communication;

[Display(Name = "ImapAttachmentDownloadFlowBlock_DisplayName", Description = "ImapAttachmentDownloadFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
public class ImapAttachmentDownloadFlowBlock : BaseResultFlowBlock
{
    [Display(Name = "ImapAttachmentDownloadFlowBlock_AssociatedImap", Description = "ImapAttachmentDownloadFlowBlock_AssociatedImap_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    [AssociatedFlowBlockResolvable]
    [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
    [FlowBloxItemsSource(Method = nameof(GetPossibleImapFlowBlocks), DisplayMember = nameof(Name))]
    public IMAPMailFetchFlowBlock AssociatedImap { get; set; }

    [Display(Name = "ImapAttachmentDownloadFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    [FlowBloxDataGrid]
    public ObservableCollection<ResultFieldByEnumValue<ImapAttachmentDownloadDestinations>> ResultFields { get; set; } = new();

    public override List<FieldElement> Fields => ResultFields
        .Where(entry => entry?.EnumValue != null && entry.ResultField != null)
        .Select(entry => entry.ResultField)
        .ToList();

    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.attachment, 16, SKColors.CornflowerBlue);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.attachment, 32, SKColors.CornflowerBlue);
    public override FlowBlockCategory GetCategory() => FlowBlockCategory.Communication;
    public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

    public List<IMAPMailFetchFlowBlock> GetPossibleImapFlowBlocks() =>
        FlowBloxRegistryProvider.GetRegistry().GetFlowBlocks<IMAPMailFetchFlowBlock>().ToList();

    public override List<string> GetDisplayableProperties() => [.. base.GetDisplayableProperties(), nameof(AssociatedImap)];

    public override void OnAfterCreate()
    {
        CreateDestinationResultField(ResultFields, ImapAttachmentDownloadDestinations.FileName);
        CreateDestinationResultField(ResultFields, ImapAttachmentDownloadDestinations.FileContent, FieldTypes.ByteArray);
        base.OnAfterCreate();
    }

    public override List<Type> NotificationTypes => [.. base.NotificationTypes, typeof(ImapAttachmentNotifications)];

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            var source = AssociatedImap ?? GetPreviousFlowBlockOnPath<IMAPMailFetchFlowBlock>(this);
            if (source == null)
                throw new InvalidOperationException("No associated IMAP fetch block could be resolved.");

            var uidValue = source.ResultFields.FirstOrDefault(entry => entry?.EnumValue == IMAPMailFetchDestinations.UniqueId)?.ResultField?.StringValue;
            if (!uint.TryParse(uidValue, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid == 0)
            {
                CreateNotification(runtime, ImapAttachmentNotifications.MailUidMissing);
                GenerateResult(runtime);
                return;
            }

            var cancellationToken = runtime.GetCancellationToken();
            using var client = new ImapClient();
            if (source.Resolve(block => block.AcceptInvalidCertificates))
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;

            var host = source.Resolve(block => block.Host, required: true).Trim();
            var port = source.Resolve(block => block.Port);
            var useSsl = source.Resolve(block => block.UseSsl);
            var userName = source.Resolve(block => block.UserName, required: true).Trim();
            var password = source.Resolve(block => block.Password);
            var folderName = source.Resolve(block => block.Folder).Trim();
            client.Connect(host, port, useSsl, cancellationToken);
            client.Authenticate(userName, password, cancellationToken);
            var folder = string.IsNullOrWhiteSpace(folderName) || folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : client.GetFolder(folderName, cancellationToken);
            folder.Open(FolderAccess.ReadOnly, cancellationToken);

            var validityValue = source.ResultFields.FirstOrDefault(entry => entry?.EnumValue == IMAPMailFetchDestinations.UidValidity)?.ResultField?.StringValue;
            if (!string.IsNullOrEmpty(validityValue) &&
                (!uint.TryParse(validityValue, NumberStyles.None, CultureInfo.InvariantCulture, out var validity) || validity != folder.UidValidity))
            {
                CreateNotification(runtime, ImapAttachmentNotifications.MailboxChanged);
                GenerateResult(runtime);
                return;
            }

            var messageUid = new UniqueId(uid);
            var summary = folder.Fetch([messageUid], MessageSummaryItems.BodyStructure, cancellationToken)
                .SingleOrDefault(item => item.UniqueId == messageUid);
            if (summary == null)
            {
                CreateNotification(runtime, ImapAttachmentNotifications.MailNotFound);
                GenerateResult(runtime);
                return;
            }

            var results = new List<Dictionary<FieldElement, string>>();
            foreach (var part in summary.Attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var entity = folder.GetBodyPart(messageUid, part, cancellationToken);
                var attachment = ImapAttachmentReader.Decode(entity, results.Count + 1, cancellationToken);
                results.Add(new ResultFieldByEnumValueResultBuilder<ImapAttachmentDownloadDestinations>()
                    .For(ImapAttachmentDownloadDestinations.FileName, attachment.FileName)
                    .For(ImapAttachmentDownloadDestinations.FileContent, Convert.ToBase64String(attachment.Content))
                    .For(ImapAttachmentDownloadDestinations.ContentType, attachment.ContentType)
                    .For(ImapAttachmentDownloadDestinations.Size, attachment.Content.LongLength.ToString(CultureInfo.InvariantCulture))
                    .Build(ResultFields));
            }

            client.Disconnect(true, cancellationToken);
            GenerateResult(runtime, results);
        });
    }

    public enum ImapAttachmentNotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Warning)]
        [Display(Name = "Mail UID missing")]
        MailUidMissing,
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "IMAP mailbox changed")]
        MailboxChanged,
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Mail no longer available")]
        MailNotFound
    }
}
