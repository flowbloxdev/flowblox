using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Util.Downloads;

namespace FlowBloxTest.Services
{
    internal sealed class RecordingMediaDownloadProvider : MediaDownloadProviderBase
    {
        public Func<CancellationToken, Task<string>> Handler { get; set; } =
            _ => throw new IOException("Simulated media provider failure.");

        public MediaDownloadRequest? LastRequest { get; private set; }
        public int InvocationCount { get; private set; }

        protected override Task<string> DownloadCoreAsync(
            MediaDownloadRequest request,
            TransferProgressReporter progress,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            InvocationCount++;
            return Handler(cancellationToken);
        }
    }
}
