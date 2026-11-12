using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Util.Downloads;

namespace FlowBloxTest.Services
{
    internal sealed class RecordingMediaDownloadProvider : MediaDownloadProviderBase
    {
        public Func<CancellationToken, Task<IReadOnlyList<MediaDownloadResult>>> Handler { get; set; } =
            _ => throw new IOException("Simulated media provider failure.");

        public MediaDownloadRequest? LastRequest { get; private set; }
        public int InvocationCount { get; private set; }

        public Func<MediaDownloadFormat, bool> FormatSupport { get; set; } = format => Enum.IsDefined(format);

        public override bool SupportsFormat(MediaDownloadFormat format) => FormatSupport(format);

        protected override Task<IReadOnlyList<MediaDownloadResult>> DownloadCoreAsync(
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
