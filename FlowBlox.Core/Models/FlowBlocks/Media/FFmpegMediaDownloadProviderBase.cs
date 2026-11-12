using System.ComponentModel.DataAnnotations;
using FFMpegCore;
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
        [FlowBloxUI(Factory = UIFactory.Association, SelectionFilterMethod = nameof(GetPossibleFFmpegTools),
            SelectionDisplayMember = nameof(FFmpegTool.Name))]
        public FFmpegTool? FFmpeg { get; set; }

        public IEnumerable<FFmpegTool> GetPossibleFFmpegTools() =>
            FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<FFmpegTool>();

        protected string ResolveFFmpegExecutablePath() => FFmpeg?.GetExecutablePath() ?? ResolveFallbackFFmpegPath();

        protected FFOptions ResolveFFmpegOptions() =>
            FFmpeg?.CreateOptions() ?? new FFOptions
            {
                BinaryFolder = Path.GetDirectoryName(ResolveFallbackFFmpegPath()) ?? string.Empty
            };

        protected virtual string ResolveFallbackFFmpegPath() =>
            OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    }
}
