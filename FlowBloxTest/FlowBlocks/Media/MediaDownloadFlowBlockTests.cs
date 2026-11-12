using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
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
                    Handler = _ => SuccessfulDownloadAsync(temporaryFile)
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
                Handler = _ => SuccessfulDownloadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"))
            };

            var results = await provider.DownloadAsync(
                CreateRequest(),
                new TransferProgressReporter(messages.Add),
                CancellationToken.None);

            Assert.IsFalse(results.Single().Success);
            Assert.AreEqual(string.Empty, results.Single().FilePath);
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

        [TestMethod]
        [DataRow(NotificationType.Warning)]
        [DataRow(NotificationType.Error)]
        [DataRow(NotificationType.None)]
        public void MixedResults_EmitAllRowsAndRespectNotificationOverride(NotificationType notificationType)
        {
            var filePath = Path.GetTempFileName();
            try
            {
                var project = new FlowBloxProject();
                FlowBloxProjectManager.Instance.ActiveProject = project;
                var start = CreateFlowBlock<StartFlowBlock>();
                var block = CreateFlowBlock<MediaDownloadFlowBlock>(start);
                block.Url = "https://example.test/playlist";
                block.OutputDirectory = Path.GetTempPath();
                block.Provider = new RecordingMediaDownloadProvider
                {
                    Handler = _ => Task.FromResult<IReadOnlyList<MediaDownloadResult>>(
                    [
                        new MediaDownloadResult
                        {
                            Success = true,
                            FilePath = filePath,
                            SourceUrl = "https://example.test/first"
                        },
                        new MediaDownloadResult
                        {
                            SourceUrl = "https://example.test/private",
                            FailureReason = MediaDownloadFailureReason.DownloadFailed,
                            ErrorMessage = "Private video."
                        },
                        new MediaDownloadResult
                        {
                            Success = true,
                            FilePath = filePath,
                            SourceUrl = "https://example.test/last"
                        }
                    ])
                };
                block.OverrideNotificationType(MediaDownloadFlowBlock.MediaDownloadNotifications.DownloadFailed, notificationType);
                var warnings = 0;
                var errors = 0;
                block.OnWarn += (_, _) => warnings++;
                block.OnError += (_, _) => errors++;

                block.Execute(new FlowBloxUnitTestRuntime(project), null);

                Assert.HasCount(3, block.GridElementResult.Results);
                var successField = block.ResultFields.Single(item => item.EnumValue == MediaDownloadDestinations.Success).ResultField;
                var successValues = block.GridElementResult.Results
                    .Select(row => row.FieldValueMappings.Single(mapping => ReferenceEquals(mapping.Field, successField)).Value)
                    .ToArray();
                CollectionAssert.AreEqual(new[] { "True", "False", "True" }, successValues);
                Assert.AreEqual(notificationType == NotificationType.Warning ? 1 : 0, warnings);
                Assert.AreEqual(notificationType == NotificationType.Error ? 1 : 0, errors);
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        [TestMethod]
        public async Task MissingFileInBatch_PreservesOtherSuccessfulResults()
        {
            var filePath = Path.GetTempFileName();
            try
            {
                var provider = new RecordingMediaDownloadProvider
                {
                    Handler = _ => Task.FromResult<IReadOnlyList<MediaDownloadResult>>(
                    [
                        new MediaDownloadResult { Success = true, FilePath = filePath },
                        new MediaDownloadResult { Success = true, FilePath = filePath + ".missing" }
                    ])
                };

                var results = await provider.DownloadAsync(CreateRequest(), new TransferProgressReporter(_ => { }), CancellationToken.None);

                Assert.IsTrue(results[0].Success);
                Assert.AreEqual(filePath, results[0].FilePath);
                Assert.IsFalse(results[1].Success);
                Assert.AreEqual(MediaDownloadFailureReason.OutputFileMissing, results[1].FailureReason);
                Assert.AreEqual(string.Empty, results[1].FilePath);
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        [TestMethod]
        public void EmptyPlaylist_EmitsEmptyResultAndCreatesWarning()
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var start = CreateFlowBlock<StartFlowBlock>();
            var block = CreateFlowBlock<MediaDownloadFlowBlock>(start);
            block.Url = "https://example.test/empty";
            block.OutputDirectory = Path.GetTempPath();
            block.Provider = new RecordingMediaDownloadProvider
            {
                Handler = _ => Task.FromResult<IReadOnlyList<MediaDownloadResult>>([])
            };
            var warnings = 0;
            block.OnWarn += (_, _) => warnings++;

            block.Execute(new FlowBloxUnitTestRuntime(project), null);

            Assert.HasCount(1, block.GridElementResult.Results);
            Assert.IsTrue(block.GridElementResult.Results[0].FieldValueMappings.All(mapping => mapping.Value == null));
            Assert.AreEqual(1, warnings);
        }

        private static Task<IReadOnlyList<MediaDownloadResult>> SuccessfulDownloadAsync(string filePath) =>
            Task.FromResult<IReadOnlyList<MediaDownloadResult>>(
            [
                new MediaDownloadResult
                {
                    Success = true,
                    FilePath = filePath
                }
            ]);

        private static async Task<IReadOnlyList<MediaDownloadResult>> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
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
