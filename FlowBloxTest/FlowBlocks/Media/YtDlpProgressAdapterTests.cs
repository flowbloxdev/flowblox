using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Util.Downloads;
using YoutubeDLSharp;

namespace FlowBloxTest.FlowBlocks.Media;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class YtDlpProgressAdapterTests
{
    [TestMethod]
    public void StatusOnlyEventAfterFullDownload_DoesNotReportZeroPercent()
    {
        var messages = new List<string>();
        var reporter = new TransferProgressReporter(messages.Add);
        var adapter = new YtDlpProgressAdapter(reporter);

        adapter.Report(new DownloadProgress(DownloadState.Downloading, progress: 1));
        adapter.Report(new DownloadProgress(DownloadState.Downloading));
        reporter.Complete();

        CollectionAssert.AreEqual(new[] { "Downloading: 100%.", "Transfer completed." }, messages);
    }

    [TestMethod]
    public void StatusOnlyEventAtStart_ReportsPhaseWithoutInventingPercentage()
    {
        var messages = new List<string>();
        var adapter = new YtDlpProgressAdapter(new TransferProgressReporter(messages.Add));

        adapter.Report(new DownloadProgress(DownloadState.Downloading));

        CollectionAssert.AreEqual(new[] { "Downloading: started." }, messages);
    }

    [TestMethod]
    public void NextStreamWithActualProgress_CanRestartAtZeroPercent()
    {
        var messages = new List<string>();
        var adapter = new YtDlpProgressAdapter(new TransferProgressReporter(messages.Add));

        adapter.Report(new DownloadProgress(DownloadState.Downloading, progress: 1));
        adapter.Report(new DownloadProgress(DownloadState.Downloading));
        adapter.Report(new DownloadProgress(DownloadState.Downloading, progress: 0, totalDownloadSize: "10MiB"));
        adapter.Report(new DownloadProgress(DownloadState.Downloading, progress: 1));

        CollectionAssert.AreEqual(new[] { "Downloading: 100%.", "Downloading: 0%.", "Downloading: 100%." }, messages);
    }
}
