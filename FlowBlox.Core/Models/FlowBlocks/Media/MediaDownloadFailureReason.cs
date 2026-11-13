namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    public enum MediaDownloadFailureReason
    {
        None,
        DownloadFailed,
        PlaylistNotSupported,
        OutputFileMissing,
        Timeout,
        RequiredComponentMissing
    }
}
