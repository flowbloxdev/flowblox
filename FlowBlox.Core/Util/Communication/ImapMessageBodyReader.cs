using MailKit;
using MimeKit;

namespace FlowBlox.Core.Util.Communication;

internal static class ImapMessageBodyReader
{
    public static MimeMessage Read(
        IMailFolder folder,
        IMessageSummary summary,
        bool includeHtmlAlternative,
        CancellationToken cancellationToken)
    {
        var plain = summary.TextBody == null
            ? null
            : folder.GetBodyPart(summary.UniqueId, summary.TextBody, cancellationToken) as TextPart;
        var html = summary.HtmlBody == null || (plain != null && !includeHtmlAlternative)
            ? null
            : folder.GetBodyPart(summary.UniqueId, summary.HtmlBody, cancellationToken) as TextPart;

        MimeEntity body = plain ?? html;
        if (plain != null && html != null)
            body = new MultipartAlternative { plain, html };

        return new MimeMessage { Body = body };
    }
}
