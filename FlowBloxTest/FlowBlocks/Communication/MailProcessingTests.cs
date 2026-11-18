using FlowBlox.Core.Models.FlowBlocks.Communication;
using FlowBlox.Core.Util.Communication;
using MimeKit;

namespace FlowBloxTest.FlowBlocks.Communication;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class MailProcessingTests
{
    [TestMethod]
    [DataRow("On Monday, Alice wrote:")]
    [DataRow("Am Montag schrieb Alice:")]
    [DataRow("On Monday,\nAlice wrote:")]
    public void PlainTextQuotes_PreserveInlineAnswersAndOriginalContent(string header)
    {
        var original = "New information\n\n" + header + "\n> Old question\n> More history\n\nMy inline answer";
        var message = new MimeMessage { Body = new TextPart("plain") { Text = original } };
        var originalContent = message.TextBody;

        var cleaned = MailContentCleaner.GetCurrentContent(message);

        Assert.Contains("New information", cleaned);
        Assert.Contains("My inline answer", cleaned);
        Assert.DoesNotContain("Old question", cleaned);
        Assert.Contains(header.ReplaceLineEndings(Environment.NewLine), cleaned);
        Assert.AreEqual(originalContent, MailContentCleaner.GetOriginalContent(message));
    }

    [TestMethod]
    [DataRow("-----Original Message-----\nOld message")]
    [DataRow("-----Ursprüngliche Nachricht-----\nAlte Nachricht")]
    [DataRow("Von: alice@example.test\nGesendet: Montag\nAn: bob@example.test\nBetreff: Old topic\n\nOld message")]
    public void PlainTextQuotes_PreserveHistoryWithoutStructuralMarkers(string history)
    {
        var message = new MimeMessage { Body = new TextPart("plain") { Text = "New information\n\n" + history } };

        Assert.AreEqual(("New information\n\n" + history).ReplaceLineEndings(Environment.NewLine), MailContentCleaner.GetCurrentContent(message));
    }

    [TestMethod]
    [DataRow("<blockquote><div>Old message</div></blockquote>")]
    [DataRow("<div class='gmail_quote'><div>Old message</div></div>")]
    [DataRow("<div class='yahoo_quoted'><p>Old message</p></div>")]
    public void HtmlQuotes_PreserveContentAfterNestedQuoteContainers(string quote)
    {
        var html = "<p>New information &amp; details</p>" + quote + "<p>My inline answer</p>";
        var message = new MimeMessage { Body = new TextPart("html") { Text = html } };

        var cleaned = MailContentCleaner.GetCurrentContent(message);

        Assert.Contains("New information & details", cleaned);
        Assert.Contains("My inline answer", cleaned);
        Assert.DoesNotContain("Old message", cleaned);
        Assert.AreEqual(html, MailContentCleaner.GetOriginalContent(message));
    }

    [TestMethod]
    public void MultipartQuotes_UseHtmlStructureInsteadOfLocalizedTextHeaders()
    {
        var alternatives = new MultipartAlternative
        {
            new TextPart("plain")
            {
                Text = "Nouvelle information\n\nLe lundi, Alice a écrit :\nAncien message"
            },
            new TextPart("html")
            {
                Text = "<p>Nouvelle information</p><blockquote>Le lundi, Alice a écrit :<p>Ancien message</p></blockquote><p>Réponse en ligne</p>"
            }
        };
        var message = new MimeMessage { Body = alternatives };

        var cleaned = MailContentCleaner.GetCurrentContent(message);

        Assert.Contains("Nouvelle information", cleaned);
        Assert.Contains("Réponse en ligne", cleaned);
        Assert.DoesNotContain("Ancien message", cleaned);
        Assert.DoesNotContain("a écrit", cleaned);
    }

    [TestMethod]
    public void PlainTextQuotes_PreserveUnrecognizedAndForwardedContent()
    {
        var text = "From: our documentation\nThis is not a reply header.\n\n-----Forwarded message-----\nRelevant forwarded text";
        var message = new MimeMessage { Body = new TextPart("plain") { Text = text } };

        Assert.AreEqual(text.ReplaceLineEndings(Environment.NewLine), MailContentCleaner.GetCurrentContent(message));
    }

}
