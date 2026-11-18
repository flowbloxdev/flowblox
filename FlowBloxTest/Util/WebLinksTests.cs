using FlowBlox.Core.Util;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class WebLinksTests
{
    [TestMethod]
    public void Links_PreserveQueryAndBalancedBracketsWithoutConsumingSentencePunctuation()
    {
        const string text = "Download (https://example.test/a_(b)?x=1&y=2). Then https://example.test/file.zip, please.";

        var links = WebLinks.Find(text).ToList();

        CollectionAssert.AreEqual(new[] { "https://example.test/a_(b)?x=1&y=2", "https://example.test/file.zip" },
            links.Select(link => text.Substring(link.Start, link.Length)).ToArray());
        Assert.AreEqual(")", text.Substring(links[0].Start + links[0].Length, 1));
    }

    [TestMethod]
    public void Links_RecognizeOnlyValidWebAddressesAndKeepTheirSourcePositions()
    {
        const string text = "file:///C:/private.txt javascript:alert(1) https://\nHTTPS://example.test/help";

        var link = WebLinks.Find(text).Single();

        Assert.AreEqual("HTTPS://example.test/help", text.Substring(link.Start, link.Length));
        Assert.AreEqual("https", link.Uri.Scheme);
    }
}
