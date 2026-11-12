using FlowBlox.Core.Models.FlowBlocks.Media;
using YoutubeDLSharp.Metadata;

namespace FlowBloxTest.FlowBlocks.Media
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class YtDlpMediaDownloadProviderTests
    {
        [TestMethod]
        public async Task DisabledPlaylist_ReturnsReasonWithoutDownloadingAnyEntry()
        {
            var results = await YtDlpMediaDownloadProvider.DownloadDiscoveredMediaAsync(
                new VideoData { Entries = [new VideoData { Url = "https://example.test/first" }] },
                false,
                "https://example.test/playlist",
                (_, _) => throw new AssertFailedException("A disabled playlist must not start a download."),
                CancellationToken.None);

            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].Success);
            Assert.AreEqual(MediaDownloadFailureReason.PlaylistNotSupported, results[0].FailureReason);
            Assert.AreEqual("https://example.test/playlist", results[0].SourceUrl);
        }

        [TestMethod]
        public async Task FailedEntry_PreservesSuccessfulDownloadsAndContinuesInPlaylistOrder()
        {
            var attemptedUrls = new List<string>();
            var metadata = new VideoData
            {
                Entries =
                [
                    new VideoData { Url = "https://example.test/first" },
                    new VideoData { Url = "https://example.test/private" },
                    new VideoData { Url = "https://example.test/last" }
                ]
            };

            var results = await YtDlpMediaDownloadProvider.DownloadDiscoveredMediaAsync(
                metadata,
                true,
                "https://example.test/playlist",
                (url, _) =>
                {
                    attemptedUrls.Add(url);
                    if (url.EndsWith("/private", StringComparison.Ordinal))
                        throw new IOException("Video is unavailable.");

                    return Task.FromResult(url.EndsWith("/first", StringComparison.Ordinal) ? "first.mp3" : "last.mp3");
                },
                CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "https://example.test/first", "https://example.test/private", "https://example.test/last" },
                attemptedUrls);
            CollectionAssert.AreEqual(new[] { true, false, true }, results.Select(result => result.Success).ToArray());
            Assert.AreEqual("first.mp3", results[0].FilePath);
            Assert.AreEqual(MediaDownloadFailureReason.DownloadFailed, results[1].FailureReason);
            Assert.AreEqual("Video is unavailable.", results[1].ErrorMessage);
            Assert.AreEqual("last.mp3", results[2].FilePath);
        }

        [TestMethod]
        public async Task MissingEntry_ProducesFailureAndDoesNotDiscardFollowingEntry()
        {
            var results = await YtDlpMediaDownloadProvider.DownloadDiscoveredMediaAsync(
                new VideoData
                {
                    Entries = [null!, new VideoData { WebpageUrl = "https://example.test/next" }]
                },
                true,
                "https://example.test/playlist",
                (_, _) => Task.FromResult("next.mp3"),
                CancellationToken.None);

            Assert.HasCount(2, results);
            Assert.IsFalse(results[0].Success);
            Assert.AreEqual("https://example.test/playlist", results[0].SourceUrl);
            Assert.IsTrue(results[1].Success);
        }

        [TestMethod]
        public async Task SingleVideo_UsesOriginalPageUrlInsteadOfTemporaryStreamUrl()
        {
            string? attemptedUrl = null;
            var results = await YtDlpMediaDownloadProvider.DownloadDiscoveredMediaAsync(
                new VideoData { Url = "https://cdn.example.test/temporary-stream" },
                false,
                "https://example.test/watch?v=123",
                (url, _) =>
                {
                    attemptedUrl = url;
                    return Task.FromResult("video.mp3");
                },
                CancellationToken.None);

            Assert.AreEqual("https://example.test/watch?v=123", attemptedUrl);
            Assert.AreEqual(attemptedUrl, results.Single().SourceUrl);
        }

        [TestMethod]
        public async Task Cancellation_StopsPlaylistInsteadOfProducingFailureRows()
        {
            using var cancellation = new CancellationTokenSource();
            var attempts = 0;
            await Assert.ThrowsAsync<OperationCanceledException>(() => YtDlpMediaDownloadProvider.DownloadDiscoveredMediaAsync(
                new VideoData
                {
                    Entries =
                    [
                        new VideoData { Url = "https://example.test/first" },
                        new VideoData { Url = "https://example.test/second" }
                    ]
                },
                true,
                "https://example.test/playlist",
                (_, token) =>
                {
                    attempts++;
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult("unused.mp3");
                },
                cancellation.Token));

            Assert.AreEqual(1, attempts);
        }
    }
}
