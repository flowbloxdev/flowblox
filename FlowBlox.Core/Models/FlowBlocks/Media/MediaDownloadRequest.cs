namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    public sealed class MediaDownloadRequest
    {
        public string Url { get; init; } = string.Empty;
        public string OutputDirectory { get; init; } = string.Empty;
        public MediaDownloadFormat Format { get; init; }
        public bool OverwriteFiles { get; init; }
        public int? AudioBitrateKbps { get; init; }
    }
}
