using System.ComponentModel.DataAnnotations;
using FFMpegCore;
using FlowBlox.Core.Attributes;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    [Display(Name = "FFmpegTool_DisplayName", Description = "FFmpegTool_Description", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("FFmpegTool_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class FFmpegTool : MediaToolBase
    {
        protected override string ExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        public FFOptions CreateOptions() => new FFOptions { BinaryFolder = ResolveBinaryDirectory() };
    }
}
