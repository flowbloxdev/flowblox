using MimeKit;

namespace FlowBlox.Core.Util.Communication;

internal static class ImapAttachmentReader
{
    public static ImapAttachmentContent Decode(MimeEntity entity, int index, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var name = entity.ContentDisposition?.FileName ?? entity.ContentType.Name;
        if (entity is MessagePart messagePart)
        {
            messagePart.Message.WriteTo(output, cancellationToken);
            name ??= $"attachment-{index}.eml";
        }
        else if (entity is MimePart mimePart)
        {
            mimePart.Content.DecodeTo(output, cancellationToken);
        }
        else
        {
            throw new InvalidDataException("The IMAP attachment is not a supported MIME part.");
        }

        name = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        return new ImapAttachmentContent
        {
            FileName = string.IsNullOrWhiteSpace(name) ? $"attachment-{index}" : name,
            ContentType = entity.ContentType.MimeType,
            Content = output.ToArray()
        };
    }
}
