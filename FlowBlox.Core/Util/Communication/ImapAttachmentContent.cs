namespace FlowBlox.Core.Util.Communication;

internal sealed class ImapAttachmentContent
{
    public string FileName { get; init; }
    public string ContentType { get; init; }
    public byte[] Content { get; init; }
}
