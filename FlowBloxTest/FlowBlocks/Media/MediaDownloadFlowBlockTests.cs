using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Downloads;
using FlowBlox.Test.Runtime;
using FlowBloxTest.Services;

namespace FlowBloxTest.FlowBlocks.Media
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class MediaDownloadFlowBlockTests : FlowBloxTestsBase
    {
        [TestMethod]
        public void Execute_FailureAfterSuccess_EmitsFailureWithoutPreviousFilePath()
        {
            var temporaryFile = Path.GetTempFileName();
            try
            {
                var project = new FlowBloxProject();
                FlowBloxProjectManager.Instance.ActiveProject = project;
                var start = CreateFlowBlock<StartFlowBlock>();
                var block = CreateFlowBlock<MediaDownloadFlowBlock>(start);
                var provider = new RecordingMediaDownloadProvider
                {
                    Handler = _ => Task.FromResult(temporaryFile)
                };
                block.Provider = provider;
                block.Url = "https://example.test/video";
                block.OutputDirectory = Path.GetTempPath();
                block.Format = MediaDownloadFormat.Mp3;
                var runtime = new FlowBloxUnitTestRuntime(project);

                block.Execute(runtime, null);
                Assert.AreEqual("True", GetResult(block, MediaDownloadDestinations.Success));
                Assert.AreEqual(temporaryFile, GetResult(block, MediaDownloadDestinations.FilePath));

                provider.Handler = _ => throw new IOException("Download failed.");
                block.Execute(runtime, null);

                Assert.AreEqual("False", GetResult(block, MediaDownloadDestinations.Success));
                Assert.AreEqual(string.Empty, GetResult(block, MediaDownloadDestinations.FilePath));
            }
            finally
            {
                File.Delete(temporaryFile);
            }
        }

        [TestMethod]
        public async Task InvalidUrl_DoesNotInvokeProvider()
        {
            var provider = new RecordingMediaDownloadProvider();
            await Assert.ThrowsAsync<ValidationException>(() => provider.DownloadAsync(
                CreateRequest("file:///C:/private.txt"),
                new TransferProgressReporter(_ => { }),
                CancellationToken.None));

            Assert.AreEqual(0, provider.InvocationCount);
        }

        [TestMethod]
        public async Task AlreadyCancelled_DoesNotStartDownload()
        {
            var provider = new RecordingMediaDownloadProvider();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.DownloadAsync(
                CreateRequest(),
                new TransferProgressReporter(_ => { }),
                cancellation.Token));

            Assert.AreEqual(0, provider.InvocationCount);
        }

        [TestMethod]
        public async Task MissingOutputFile_IsNotReportedAsCompleted()
        {
            var messages = new List<string>();
            var provider = new RecordingMediaDownloadProvider
            {
                Handler = _ => Task.FromResult(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"))
            };

            await Assert.ThrowsAsync<IOException>(() => provider.DownloadAsync(
                CreateRequest(),
                new TransferProgressReporter(messages.Add),
                CancellationToken.None));

            CollectionAssert.DoesNotContain(messages, "Transfer completed.");
        }

        [TestMethod]
        public async Task Timeout_CancelsProviderAndIsDistinguishedFromUserCancellation()
        {
            var provider = new RecordingMediaDownloadProvider
            {
                TimeoutSeconds = 1,
                Handler = WaitForCancellationAsync
            };

            await Assert.ThrowsAsync<TimeoutException>(() => provider.DownloadAsync(
                CreateRequest(),
                new TransferProgressReporter(_ => { }),
                CancellationToken.None));
        }

        private static async Task<string> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return string.Empty;
        }

        private static MediaDownloadRequest CreateRequest(string url = "https://example.test/video") =>
            new MediaDownloadRequest
            {
                Url = url,
                OutputDirectory = Path.GetTempPath(),
                Format = MediaDownloadFormat.Mp3
            };

        private static string GetResult(MediaDownloadFlowBlock block, MediaDownloadDestinations destination)
        {
            var field = block.ResultFields.Single(item => item.EnumValue == destination).ResultField;
            return block.GridElementResult.Results.Last().FieldValueMappings
                .Single(mapping => ReferenceEquals(mapping.Field, field))
                .Value;
        }
    }
}
