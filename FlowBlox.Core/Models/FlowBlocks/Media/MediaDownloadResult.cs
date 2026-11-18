using FlowBlox.Core.Exceptions;
namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    public sealed class MediaDownloadResult
    {
        public bool Success { get; init; }
        public string FilePath { get; init; } = string.Empty;
        public string SourceUrl { get; init; } = string.Empty;
        public string ErrorMessage { get; init; } = string.Empty;
        public ExecutionEnvironmentPrerequisiteException? ExecutionEnvironmentPrerequisiteFailure { get; init; }
        public MediaDownloadFailureReason FailureReason { get; init; }
    }
}
