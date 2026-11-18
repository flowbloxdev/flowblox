using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Communication
{
    public enum IMAPMailFetchDestinations
    {
        [Display(Name = "IMAPMailFetchDestinations_Subject", ResourceType = typeof(FlowBloxTexts))]
        Subject = 0,

        [Display(Name = "IMAPMailFetchDestinations_Message", ResourceType = typeof(FlowBloxTexts))]
        Message = 1,

        [Display(Name = "IMAPMailFetchDestinations_From", ResourceType = typeof(FlowBloxTexts))]
        From = 2,

        [Display(Name = "IMAPMailFetchDestinations_To", ResourceType = typeof(FlowBloxTexts))]
        To = 3,

        [Display(Name = "IMAPMailFetchDestinations_Date", ResourceType = typeof(FlowBloxTexts))]
        Date = 4,

        [Display(Name = "IMAPMailFetchDestinations_MessageId", ResourceType = typeof(FlowBloxTexts))]
        MessageId = 5,

        [Display(Name = "IMAPMailFetchDestinations_UniqueId", ResourceType = typeof(FlowBloxTexts))]
        UniqueId = 6,

        [Display(Name = "IMAPMailFetchDestinations_IsRead", ResourceType = typeof(FlowBloxTexts))]
        IsRead = 7,

        [Display(Name = "IMAPMailFetchDestinations_OriginalMessage", ResourceType = typeof(FlowBloxTexts))]
        OriginalMessage = 8,

        [Display(Name = "IMAPMailFetchDestinations_UidValidity", ResourceType = typeof(FlowBloxTexts))]
        UidValidity = 9
    }
}
