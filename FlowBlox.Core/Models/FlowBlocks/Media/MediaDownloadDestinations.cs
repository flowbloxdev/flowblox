using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Media
{
    public enum MediaDownloadDestinations
    {
        [Display(Name = "MediaDownloadDestinations_Success", ResourceType = typeof(FlowBloxTexts))]
        Success,
        [Display(Name = "MediaDownloadDestinations_FilePath", ResourceType = typeof(FlowBloxTexts))]
        FilePath,
        [Display(Name = "MediaDownloadDestinations_FileName", ResourceType = typeof(FlowBloxTexts))]
        FileName,
        [Display(Name = "MediaDownloadDestinations_FileSize", ResourceType = typeof(FlowBloxTexts))]
        FileSize,
        [Display(Name = "MediaDownloadDestinations_SourceUrl", ResourceType = typeof(FlowBloxTexts))]
        SourceUrl
    }
}
