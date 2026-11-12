using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    public enum MediaDownloadFormat
    {
        [Display(Name = "MediaDownloadFormat_Video", ResourceType = typeof(FlowBloxTexts))]
        Video,
        [Display(Name = "MediaDownloadFormat_Audio", ResourceType = typeof(FlowBloxTexts))]
        Audio,
        [Display(Name = "MediaDownloadFormat_Mp3", ResourceType = typeof(FlowBloxTexts))]
        Mp3,
        [Display(Name = "MediaDownloadFormat_M4a", ResourceType = typeof(FlowBloxTexts))]
        M4a,
        [Display(Name = "MediaDownloadFormat_Opus", ResourceType = typeof(FlowBloxTexts))]
        Opus
    }
}
