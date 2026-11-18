using FlowBlox.Core.Util.Downloads;
using YoutubeDLSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    internal sealed class YtDlpProgressAdapter : IProgress<DownloadProgress>
    {
        private readonly TransferProgressReporter _reporter;

        public YtDlpProgressAdapter(TransferProgressReporter reporter)
        {
            _reporter = reporter;
        }

        public void Report(DownloadProgress value)
        {
            switch (value.State)
            {
                case DownloadState.PreProcessing:
                    _reporter.Report("Preparing media download");
                    break;
                case DownloadState.Downloading:
                    // Status-only events use the default Progress = 0; they are not a transfer restart.
                    var hasProgress = value.Progress != 0
                        || !string.IsNullOrWhiteSpace(value.TotalDownloadSize)
                        || !string.IsNullOrWhiteSpace(value.DownloadSpeed)
                        || !string.IsNullOrWhiteSpace(value.ETA);
                    _reporter.Report("Downloading", hasProgress ? value.Progress : null);
                    break;
                case DownloadState.PostProcessing:
                    _reporter.Report("Converting media");
                    break;
                // Success is verified against the output file by the provider base.
            }
        }
    }
}
