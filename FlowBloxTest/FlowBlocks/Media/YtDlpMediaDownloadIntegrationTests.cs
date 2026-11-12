using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Util.Downloads;
using FlowBloxTest.Services;

namespace FlowBloxTest.FlowBlocks.Media
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.IntegrationTests)]
    public sealed class YtDlpMediaDownloadIntegrationTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task HtmlPlaylist_RespectsOptInAndPreservesDownloadsAcrossConversionFailure(bool downloadPlaylist)
        {
            var ytDlpPath = Environment.GetEnvironmentVariable("FLOWBLOX_YTDLP_PATH");
            var ffmpegPath = Environment.GetEnvironmentVariable("FLOWBLOX_FFMPEG_PATH");
            if (!File.Exists(ytDlpPath) || !File.Exists(ffmpegPath))
                Assert.Inconclusive("Set FLOWBLOX_YTDLP_PATH and FLOWBLOX_FFMPEG_PATH to run external-tool integration tests.");

            using var server = new MediaPlaylistTestServer();
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxPlaylist-" + Guid.NewGuid().ToString("N"));
            try
            {
                var provider = new YtDlpMediaDownloadProvider
                {
                    ExecutablePath = ytDlpPath,
                    FFmpegPath = ffmpegPath,
                    DownloadPlaylist = downloadPlaylist,
                    TimeoutSeconds = 60
                };
                var results = await provider.DownloadAsync(
                    new MediaDownloadRequest
                    {
                        Url = server.Url,
                        OutputDirectory = directory,
                        Format = MediaDownloadFormat.Mp3
                    },
                    new TransferProgressReporter(_ => { }),
                    CancellationToken.None);

                if (!downloadPlaylist)
                {
                    Assert.HasCount(1, results);
                    Assert.AreEqual(MediaDownloadFailureReason.PlaylistNotSupported, results[0].FailureReason);
                    Assert.HasCount(0, Directory.GetFiles(directory));
                    return;
                }

                Assert.HasCount(3, results);
                CollectionAssert.AreEqual(new[] { true, false, true }, results.Select(result => result.Success).ToArray(),
                    string.Join(Environment.NewLine, results.Select(result => result.ErrorMessage)));
                Assert.AreEqual(MediaDownloadFailureReason.DownloadFailed, results[1].FailureReason);
                Assert.IsTrue(File.Exists(results[0].FilePath));
                Assert.IsTrue(File.Exists(results[2].FilePath));
                Assert.AreNotEqual(results[0].FilePath, results[2].FilePath);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }
}
