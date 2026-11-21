using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Communication;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Validations;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FlowBlox.Core.Models.FlowBlocks.Communication
{
    [FlowBloxUIGroup("IMAPMailFetchFlowBlock_Groups_Filter", 0)]
    [FlowBloxUIGroup("IMAPMailFetchFlowBlock_Groups_Behavior", 1)]
    [Display(Name = "IMAPMailFetchFlowBlock_DisplayName", Description = "IMAPMailFetchFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("IMAPMailFetchFlowBlock_SpecialExplanation_Usage", Icon = SpecialExplanationIcon.Information)]
    public class IMAPMailFetchFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "IMAPMailFetchFlowBlock_Host", Description = "IMAPMailFetchFlowBlock_Host_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public string Host { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_Port", Description = "IMAPMailFetchFlowBlock_Port_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public int Port { get; set; } = 993;

        [Display(Name = "IMAPMailFetchFlowBlock_UseSsl", Description = "IMAPMailFetchFlowBlock_UseSsl_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public bool UseSsl { get; set; } = true;

        [Display(Name = "IMAPMailFetchFlowBlock_AcceptInvalidCertificates", Description = "IMAPMailFetchFlowBlock_AcceptInvalidCertificates_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public bool AcceptInvalidCertificates { get; set; }

        [Required]
        [Display(Name = "IMAPMailFetchFlowBlock_UserName", Description = "IMAPMailFetchFlowBlock_UserName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public string UserName { get; set; }

        [Required]
        [Display(Name = "IMAPMailFetchFlowBlock_Password", Description = "IMAPMailFetchFlowBlock_Password_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxTextBox]
        public string Password { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_Folder", Description = "IMAPMailFetchFlowBlock_Folder_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        public string Folder { get; set; } = "INBOX";

        [Display(Name = "IMAPMailFetchFlowBlock_OnlyUnread", Description = "IMAPMailFetchFlowBlock_OnlyUnread_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Filter", Order = 0)]
        public bool OnlyUnread { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_StartDate", Description = "IMAPMailFetchFlowBlock_StartDate_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Filter", Order = 1)]
        public DateTime? StartDate { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_EndDate", Description = "IMAPMailFetchFlowBlock_EndDate_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Filter", Order = 2)]
        public DateTime? EndDate { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_SearchText", Description = "IMAPMailFetchFlowBlock_SearchText_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Filter", Order = 3)]
        [FlowBloxUI(ReadOnlyMethod = nameof(IsSearchTextReadOnly))]
        [DependsOnProperty(MemberName = nameof(SearchExpression))]
        [CustomValidation(typeof(ImapSearchValidator), nameof(ImapSearchValidator.Validate))]
        public string SearchText { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_SearchExpression", Description = "IMAPMailFetchFlowBlock_SearchExpression_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Filter", Order = 4)]
        [FlowBloxUI(ReadOnlyMethod = nameof(IsSearchExpressionReadOnly))]
        [DependsOnProperty(MemberName = nameof(SearchText))]
        [FlowBloxComboBox(DropDownMode = DropDownMode.Suggestion)]
        [FlowBloxItemsSource(Method = nameof(GetSearchExpressionSuggestions))]
        [CustomValidation(typeof(ImapSearchValidator), nameof(ImapSearchValidator.Validate))]
        public string SearchExpression { get; set; }

        public bool IsSearchTextReadOnly() => !string.IsNullOrWhiteSpace(SearchExpression);
        public bool IsSearchExpressionReadOnly() => !string.IsNullOrWhiteSpace(SearchText);

        public IEnumerable<string> GetSearchExpressionSuggestions() =>
        [
            "OR TEXT \"invoice\" TEXT \"receipt\"",
            "OR TEXT \"invoice\" (OR TEXT \"receipt\" TEXT \"payment\")",
            "SUBJECT \"invoice\"",
            "FROM \"sender@example.com\"",
            "TEXT \"receipt\" NOT SUBJECT \"newsletter\""
        ];

        [Range(1, 5000)]
        [Display(Name = "IMAPMailFetchFlowBlock_MaxResults", Description = "IMAPMailFetchFlowBlock_MaxResults_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Filter", Order = 5)]
        public int MaxResults { get; set; } = 100;

        [Display(Name = "IMAPMailFetchFlowBlock_MarkAsRead", Description = "IMAPMailFetchFlowBlock_MarkAsRead_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Behavior", Order = 0)]
        public bool MarkAsRead { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_DeleteAfterFetch", Description = "IMAPMailFetchFlowBlock_DeleteAfterFetch_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Behavior", Order = 1)]
        public bool DeleteAfterFetch { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_ExcludeQuotedHistory", Description = "IMAPMailFetchFlowBlock_ExcludeQuotedHistory_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "IMAPMailFetchFlowBlock_Groups_Behavior", Order = 2)]
        public bool ExcludeQuotedHistory { get; set; }

        [Display(Name = "IMAPMailFetchFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 20)]
        [FlowBloxDataGrid]
        public ObservableCollection<ResultFieldByEnumValue<IMAPMailFetchDestinations>> ResultFields { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.inbox_arrow_down_outline, 16, SKColors.CornflowerBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.inbox_arrow_down_outline, 32, SKColors.CornflowerBlue);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Communication;

        public override void OnAfterCreate()
        {
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return ResultFields
                    .Where(x => x.EnumValue != null)
                    .Select(x => x.ResultField)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Host));
            properties.Add(nameof(Port));
            properties.Add(nameof(UseSsl));
            properties.Add(nameof(UserName));
            properties.Add(nameof(Folder));
            properties.Add(nameof(OnlyUnread));
            properties.Add(nameof(StartDate));
            properties.Add(nameof(EndDate));
            properties.Add(nameof(SearchText));
            properties.Add(nameof(SearchExpression));
            properties.Add(nameof(MaxResults));
            properties.Add(nameof(MarkAsRead));
            properties.Add(nameof(DeleteAfterFetch));
            return properties;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(IMAPMailFetchNotifications));
                return notificationTypes;
            }
        }

        private void CreateDefaultResultFields()
        {
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.Subject, FieldTypes.Text);
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.Message, FieldTypes.Text);
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.From, FieldTypes.Text);
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.Date, FieldTypes.Text);
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.UniqueId, FieldTypes.Text);
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.UidValidity, FieldTypes.Text);
            CreateDestinationResultField(ResultFields, IMAPMailFetchDestinations.IsRead, FieldTypes.Boolean);
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                var port = this.Resolve(x => x.Port);
                var useSsl = this.Resolve(x => x.UseSsl);
                var host = this.Resolve(owner => owner.Host).Trim();
                var userName = this.Resolve(owner => owner.UserName).Trim();
                var password = this.Resolve(owner => owner.Password);
                var folderName = (Folder == null ? "INBOX" : this.Resolve(x => x.Folder)).Trim();
                var searchText = this.Resolve(owner => owner.SearchText).Trim();
                var searchExpression = this.Resolve(owner => owner.SearchExpression).Trim();
                var startDate = this.Resolve(x => x.StartDate)?.Date;
                var endDate = this.Resolve(x => x.EndDate)?.Date;
                if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
                {
                    runtime.Report(
                        $"{FlowBloxTexts.IMAPMailFetchFlowBlock_Validation_DateRange} StartDate='{startDate.Value:yyyy-MM-dd}', EndDate='{endDate.Value:yyyy-MM-dd}'.",
                        FlowBloxLogLevel.Error);
                    CreateNotification(runtime, IMAPMailFetchNotifications.InvalidDateRange);
                    GenerateResult(runtime);
                    return;
                }

                if (string.IsNullOrWhiteSpace(host))
                {
                    CreateNotification(runtime, IMAPMailFetchNotifications.HostIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (string.IsNullOrWhiteSpace(userName))
                {
                    CreateNotification(runtime, IMAPMailFetchNotifications.UserNameIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = "INBOX";

                var cancellationToken = runtime.GetCancellationToken();
                var canWrite = this.Resolve(x => x.MarkAsRead) || this.Resolve(x => x.DeleteAfterFetch);

                using var client = new ImapClient();
                if (this.Resolve(x => x.AcceptInvalidCertificates))
                    client.ServerCertificateValidationCallback = (_, _, _, _) => true;

                client.Connect(host, port, useSsl, cancellationToken);
                client.Authenticate(userName, password, cancellationToken);

                var folder = string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase)
                    ? client.Inbox
                    : client.GetFolder(folderName);

                if (folder == null)
                    throw new InvalidOperationException("The selected IMAP folder could not be resolved.");

                folder.Open(canWrite ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, cancellationToken);

                var query = BuildSearchQuery(startDate, endDate, searchText, this.Resolve(x => x.OnlyUnread));
                var uids = string.IsNullOrWhiteSpace(searchText) && !string.IsNullOrWhiteSpace(searchExpression)
                    ? ((IImapFolder)folder).Search(
                        BuildRawSearchQuery(startDate, endDate, searchExpression, this.Resolve(x => x.OnlyUnread)),
                        cancellationToken).UniqueIds
                    : folder.Search(query, cancellationToken);

                if (uids.Count == 0)
                {
                    CreateNotification(runtime, IMAPMailFetchNotifications.NoMessagesFound);
                    GenerateResult(runtime);
                    client.Disconnect(true, cancellationToken);
                    return;
                }

                var summaryItems = MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                    MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure;
                var maxResults = this.Resolve(x => x.MaxResults);
                var orderedUids = (maxResults > 0 ? uids.Take(maxResults) : uids).ToList();
                var summaries = folder.Fetch(orderedUids, summaryItems, cancellationToken)
                    .Where(summary => summary != null && summary.UniqueId.IsValid).ToList();

                var wasUnreadBeforeFetch = new List<UniqueId>();
                var includesMessage = ResultFields.Any(x => x?.EnumValue == IMAPMailFetchDestinations.Message);
                var includesOriginal = ResultFields.Any(x => x?.EnumValue == IMAPMailFetchDestinations.OriginalMessage);
                var excludeQuotedHistory = this.Resolve(x => x.ExcludeQuotedHistory);
                var resultMap = new List<Dictionary<FieldElement, string>>(orderedUids.Count);

                foreach (var summary in summaries)
                {
                    var uid = summary.UniqueId;

                    var flags = summary.Flags ?? MessageFlags.None;
                    var isReadBefore = flags.HasFlag(MessageFlags.Seen);
                    if (!isReadBefore)
                        wasUnreadBeforeFetch.Add(uid);

                    var envelope = summary.Envelope;
                    var subject = envelope?.Subject ?? string.Empty;
                    var from = string.Join("; ", envelope?.From?.Mailboxes?.Select(x => x.Address) ?? Enumerable.Empty<string>());
                    var to = string.Join("; ", envelope?.To?.Mailboxes?.Select(x => x.Address) ?? Enumerable.Empty<string>());
                    var date = summary.InternalDate?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty;
                    var messageId = envelope?.MessageId ?? string.Empty;
                    var message = string.Empty;
                    var originalMessage = string.Empty;

                    if (includesMessage || includesOriginal)
                    {
                        using var mimeMessage = ImapMessageBodyReader.Read(
                            folder, summary, excludeQuotedHistory, cancellationToken);
                        originalMessage = MailContentCleaner.GetOriginalContent(mimeMessage);
                        message = excludeQuotedHistory
                            ? MailContentCleaner.GetCurrentContent(mimeMessage)
                            : originalMessage;
                    }

                    var row = new ResultFieldByEnumValueResultBuilder<IMAPMailFetchDestinations>()
                        .For(IMAPMailFetchDestinations.Subject, subject)
                        .For(IMAPMailFetchDestinations.Message, message)
                        .For(IMAPMailFetchDestinations.OriginalMessage, originalMessage)
                        .For(IMAPMailFetchDestinations.From, from)
                        .For(IMAPMailFetchDestinations.To, to)
                        .For(IMAPMailFetchDestinations.Date, date)
                        .For(IMAPMailFetchDestinations.MessageId, messageId)
                        .For(IMAPMailFetchDestinations.UniqueId, uid.Id.ToString(CultureInfo.InvariantCulture))
                        .For(IMAPMailFetchDestinations.UidValidity, folder.UidValidity.ToString(CultureInfo.InvariantCulture))
                        .For(IMAPMailFetchDestinations.IsRead, isReadBefore.ToString(CultureInfo.InvariantCulture))
                        .Build(ResultFields);

                    resultMap.Add(row);
                }

                if (!this.Resolve(x => x.MarkAsRead) && canWrite && wasUnreadBeforeFetch.Count > 0)
                    folder.RemoveFlags(wasUnreadBeforeFetch, MessageFlags.Seen, true, cancellationToken);

                if (this.Resolve(x => x.DeleteAfterFetch) && canWrite && orderedUids.Count > 0)
                {
                    folder.AddFlags(orderedUids, MessageFlags.Deleted, true, cancellationToken);
                    folder.Expunge(cancellationToken);
                }

                GenerateResult(runtime, resultMap);
                client.Disconnect(true, cancellationToken);
            });
        }

        internal static string BuildRawSearchQuery(DateTime? startDate, DateTime? endDate, string expression, bool onlyUnread)
        {
            // Preserve OR/NOT grouping when adding the block's independent filters.
            var criteria = new List<string> { $"({expression})" };
            if (onlyUnread)
                criteria.Add("UNSEEN");
            if (startDate.HasValue)
                criteria.Add("SINCE " + startDate.Value.Date.ToString("d-MMM-yyyy", CultureInfo.InvariantCulture));
            if (endDate.HasValue)
                criteria.Add("BEFORE " + endDate.Value.Date.AddDays(1).ToString("d-MMM-yyyy", CultureInfo.InvariantCulture));
            return string.Join(" ", criteria);
        }

        private SearchQuery BuildSearchQuery(DateTime? startDate, DateTime? endDate, string searchText, bool onlyUnread)
        {
            var query = SearchQuery.All;

            if (onlyUnread)
                query = query.And(SearchQuery.NotSeen);

            if (startDate.HasValue)
                query = query.And(SearchQuery.DeliveredAfter(startDate.Value.Date));

            if (endDate.HasValue)
                query = query.And(SearchQuery.DeliveredBefore(endDate.Value.Date.AddDays(1)));

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var textQuery = SearchQuery.Or(
                    SearchQuery.SubjectContains(searchText),
                    SearchQuery.BodyContains(searchText));
                query = query.And(textQuery);
            }

            return query;
        }

        public enum IMAPMailFetchNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "IMAP host is empty")]
            HostIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "IMAP user name is empty")]
            UserNameIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "No messages found")]
            NoMessagesFound,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "Invalid date range")]
            InvalidDateRange
        }
    }
}
