using System.ComponentModel.DataAnnotations;
using FFMpegCore;
using FlowBlox.Core.Util.Execution;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Provider;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "FFmpegMediaDownloadProviderBase_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("FFmpegMediaDownloadProviderBase_DisplayName_Plural", typeof(FlowBloxTexts))]
    public abstract class FFmpegMediaDownloadProviderBase : MediaDownloadProviderBase
    {
        [Display(Name = "FFmpegMediaDownloadProviderBase_FFmpeg", Description = "FFmpegMediaDownloadProviderBase_FFmpeg_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = -1)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleFFmpegTools), DisplayMember = nameof(FFmpegTool.Name))]
        public FFmpegTool? FFmpeg { get; set; }

        public IEnumerable<FFmpegTool> GetPossibleFFmpegTools() =>
            FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<FFmpegTool>();

        protected virtual string? AdditionalBinaryDirectory => null;

        /// <inheritdoc />
        public override void ValidateExecutionPrerequisites()
        {
            var ffmpegPath = ExecutionPrerequisites.RequireExecutable<FFmpegTool>(
                "FFmpeg",
                ResolveFFmpegExecutablePath());
            var ffprobeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            ExecutionPrerequisites.RequireExecutable<FFmpegTool>(
                "FFprobe",
                Path.Combine(Path.GetDirectoryName(ffmpegPath)!, ffprobeName));
        }

        protected string ResolveFFmpegExecutablePath()
        {
            var configuredPath = FFmpeg?.GetExecutablePath() ?? ResolveFallbackFFmpegPath();
            return ExecutionPrerequisites.FindExecutable(configuredPath, AdditionalBinaryDirectory) ?? configuredPath;
        }

        protected FFOptions ResolveFFmpegOptions() => new FFOptions
        {
            BinaryFolder = Path.GetDirectoryName(ResolveFFmpegExecutablePath())!
        };

        protected virtual string ResolveFallbackFFmpegPath() =>
            OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    }
}
