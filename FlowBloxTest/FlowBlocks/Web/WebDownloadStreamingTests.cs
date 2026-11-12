using System.Collections.ObjectModel;
using FlowBlox.Core.Models.FlowBlocks.WebBrowser;
using FlowBlox.Core.Util.Downloads;
using FlowBloxTest.Services;
using NSubstitute;
using OpenQA.Selenium;

namespace FlowBloxTest.FlowBlocks.Web
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class WebDownloadStreamingTests
    {
        [TestMethod]
        public void HttpDownload_StreamsToFileAndReportsCompletionPercentage()
        {
            var body = Enumerable.Repeat((byte)42, 200000).ToArray();
            using var server = new MediaDownloadTestServer(body);
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxDownload-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "result.bin");
            var messages = new List<string>();
            try
            {
                var handler = CreateHandler(server.Url, messages.Add);
                var result = handler.DownloadFile(
                    "a", WebEventSelectionMode.CssSelector, WebDownloadMode.HttpRequest, path, string.Empty);

                Assert.IsTrue(result.Success);
                CollectionAssert.AreEqual(body, File.ReadAllBytes(path));
                Assert.IsTrue(messages.Any(message => message.Contains("100%", StringComparison.Ordinal)));
                Assert.HasCount(1, Directory.GetFiles(directory));
            }
            finally
            {
                DeleteTestFiles(directory);
            }
        }

        [TestMethod]
        public void TruncatedDownload_PreservesPreviousTargetAndRemovesTemporaryFile()
        {
            using var server = new MediaDownloadTestServer([1, 2, 3], truncate: true);
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxDownload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "result.bin");
            File.WriteAllText(path, "previous content");
            try
            {
                var handler = CreateHandler(server.Url, _ => { });
                var result = handler.DownloadFile(
                    "a", WebEventSelectionMode.CssSelector, WebDownloadMode.HttpRequest, path, string.Empty);

                Assert.IsFalse(result.Success);
                Assert.AreEqual("previous content", File.ReadAllText(path));
                Assert.HasCount(1, Directory.GetFiles(directory));
            }
            finally
            {
                DeleteTestFiles(directory);
            }
        }

        private static WebBrowserDownloadFileHandler CreateHandler(string url, Action<string> report)
        {
            var driver = Substitute.For<IWebDriver>();
            var element = Substitute.For<IWebElement>();
            var options = Substitute.For<IOptions>();
            var cookies = Substitute.For<ICookieJar>();
            driver.FindElement(Arg.Any<By>()).Returns(element);
            element.GetAttribute("href").Returns(url);
            driver.Manage().Returns(options);
            options.Cookies.Returns(cookies);
            cookies.AllCookies.Returns(new ReadOnlyCollection<Cookie>([]));
            return new WebBrowserDownloadFileHandler(driver, 10, new TransferProgressReporter(report));
        }

        private static void DeleteTestFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.GetFiles(directory))
                File.Delete(file);

            Directory.Delete(directory);
        }
    }
}
