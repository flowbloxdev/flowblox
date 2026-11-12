using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Util.Downloads;

namespace FlowBloxTest.FlowBlocks.Media
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class FFmpegMediaDownloadProviderTests
    {
        [TestMethod]
        [DataRow("../outside")]
        [DataRow("..\\outside")]
        [DataRow(".")]
        [DataRow("..")]
        public void OutputName_CannotEscapeConfiguredDirectory(string outputName)
        {
            var request = new MediaDownloadRequest
            {
                Url = "https://example.test/podcast.mp3",
                Format = MediaDownloadFormat.Mp3
            };

            Assert.Throws<ValidationException>(() =>
                FFmpegMediaDownloadProvider.CreateOutputPath(request, Path.GetTempPath(), outputName));
        }

        [TestMethod]
        public void AutomaticOutputName_DistinguishesSourcesWithSameFileName()
        {
            var first = new MediaDownloadRequest
            {
                Url = "https://example.test/episode.mp3?id=1",
                Format = MediaDownloadFormat.Mp3
            };
            var second = new MediaDownloadRequest
            {
                Url = "https://example.test/episode.mp3?id=2",
                Format = MediaDownloadFormat.Mp3
            };

            var firstPath = FFmpegMediaDownloadProvider.CreateOutputPath(first, Path.GetTempPath(), string.Empty);
            var secondPath = FFmpegMediaDownloadProvider.CreateOutputPath(second, Path.GetTempPath(), string.Empty);

            Assert.AreNotEqual(firstPath, secondPath);
        }

        [TestMethod]
        public async Task ExistingOutput_WithoutOverwrite_IsReusedWithoutStartingFFmpeg()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FlowBloxMedia-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "episode.mp3");
            File.WriteAllText(path, "existing media");
            try
            {
                var provider = new FFmpegMediaDownloadProvider
                {
                    OutputName = "episode",
                    FFmpeg = new FFmpegTool { BinaryDirectory = Path.Combine(directory, "not-installed") }
                };
                var result = await provider.DownloadAsync(
                    new MediaDownloadRequest
                    {
                        Url = "https://example.test/episode.mp3",
                        OutputDirectory = directory,
                        Format = MediaDownloadFormat.Mp3
                    },
                    new TransferProgressReporter(_ => { }),
                    CancellationToken.None);

                Assert.HasCount(1, result);
                Assert.IsTrue(result[0].Success);
                Assert.AreEqual(path, result[0].FilePath);
                Assert.AreEqual("existing media", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}
