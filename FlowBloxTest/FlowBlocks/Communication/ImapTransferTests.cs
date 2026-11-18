using FlowBlox.Core.Util.Communication;
using MailKit;
using MimeKit;
using NSubstitute;
using System.Text;

namespace FlowBloxTest.FlowBlocks.Communication;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ImapTransferTests
{
    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(true, 2)]
    public void MessageBody_ReadsOnlyRequestedTextPartsWithoutFetchingTheMessageOrAttachments(bool includeHtml, int expectedParts)
    {
        var folder = Substitute.For<IMailFolder>();
        var summary = Substitute.For<IMessageSummary>();
        var uid = new UniqueId(42);
        var plain = new BodyPartText { PartSpecifier = "1", ContentType = new ContentType("text", "plain") };
        var html = new BodyPartText { PartSpecifier = "2", ContentType = new ContentType("text", "html") };
        var attachment = new BodyPartBasic { PartSpecifier = "3", ContentType = new ContentType("application", "pdf") };
        summary.UniqueId.Returns(uid);
        summary.TextBody.Returns(plain);
        summary.HtmlBody.Returns(html);
        summary.Attachments.Returns(new[] { attachment });
        folder.GetBodyPart(uid, plain, CancellationToken.None, null).Returns(new TextPart("plain") { Text = "Current message" });
        folder.GetBodyPart(uid, html, CancellationToken.None, null).Returns(new TextPart("html") { Text = "<p>Current message</p>" });

        using var result = ImapMessageBodyReader.Read(folder, summary, includeHtml, CancellationToken.None);

        Assert.AreEqual("Current message", result.TextBody);
        Assert.AreEqual(includeHtml, result.HtmlBody != null);
        Assert.AreEqual(expectedParts, folder.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IMailFolder.GetBodyPart)));
        folder.DidNotReceiveWithAnyArgs().GetMessage(default(UniqueId), default, null);
        folder.DidNotReceive().GetBodyPart(uid, attachment, CancellationToken.None, null);
    }

    [TestMethod]
    public void HtmlOnlyMessage_StillDownloadsItsTextBodyWithoutAttachments()
    {
        var folder = Substitute.For<IMailFolder>();
        var summary = Substitute.For<IMessageSummary>();
        var part = new BodyPartText { PartSpecifier = "1", ContentType = new ContentType("text", "html") };
        summary.UniqueId.Returns(new UniqueId(5));
        summary.HtmlBody.Returns(part);
        folder.GetBodyPart(summary.UniqueId, part, CancellationToken.None, null)
            .Returns(new TextPart("html") { Text = "<p>Mail</p>" });

        using var result = ImapMessageBodyReader.Read(folder, summary, false, CancellationToken.None);

        Assert.AreEqual("<p>Mail</p>", result.HtmlBody);
        folder.DidNotReceiveWithAnyArgs().GetMessage(default(UniqueId), default, null);
    }

    [TestMethod]
    public void AttachmentContent_IsDecodedAndItsNameCannotContainADirectory()
    {
        var bytes = new byte[] { 0, 255, 16, 128 };
        using var entity = new MimePart("application", "pdf")
        {
            FileName = "..\\invoice.pdf",
            Content = new MimeContent(new MemoryStream(Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes))), ContentEncoding.Base64)
        };

        var result = ImapAttachmentReader.Decode(entity, 1, CancellationToken.None);

        Assert.AreEqual("invoice.pdf", result.FileName);
        Assert.AreEqual("application/pdf", result.ContentType);
        CollectionAssert.AreEqual(bytes, result.Content);
    }

    [TestMethod]
    public void AttachedMessage_IsReturnedAsAnEmlFile()
    {
        using var entity = new MessagePart
        {
            Message = new MimeMessage { Subject = "Attached mail", Body = new TextPart("plain") { Text = "Details" } }
        };

        var result = ImapAttachmentReader.Decode(entity, 2, CancellationToken.None);
        using var stream = new MemoryStream(result.Content);
        using var message = MimeMessage.Load(stream);

        Assert.AreEqual("attachment-2.eml", result.FileName);
        Assert.AreEqual("Attached mail", message.Subject);
        Assert.AreEqual("Details", message.TextBody.TrimEnd('\r', '\n'));
    }
}
