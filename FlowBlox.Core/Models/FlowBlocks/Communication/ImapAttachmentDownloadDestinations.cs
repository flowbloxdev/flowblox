using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Communication;

public enum ImapAttachmentDownloadDestinations
{
    [Display(Name = "PropertyNames_FileName", ResourceType = typeof(FlowBloxTexts))]
    FileName,
    [Display(Name = "ImapAttachmentDownloadDestinations_FileContent", ResourceType = typeof(FlowBloxTexts))]
    FileContent,
    [Display(Name = "ImapAttachmentDownloadDestinations_ContentType", ResourceType = typeof(FlowBloxTexts))]
    ContentType,
    [Display(Name = "ImapAttachmentDownloadDestinations_Size", ResourceType = typeof(FlowBloxTexts))]
    Size
}
