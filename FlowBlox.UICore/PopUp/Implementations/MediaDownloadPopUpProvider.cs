using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public sealed class MediaDownloadPopUpProvider :
        ComponentPopupProviderBase<MediaDownloadFlowBlock>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpMediaDownloadQuickStartCompleted;
        protected override string WindowTitle => MediaDownloadQuickStartContent.Text("Download_WindowTitle");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(MediaDownloadFlowBlock target)
        {
            return
            [
                MediaDownloadQuickStartContent.Create(
                    "Download_ProviderHeadline",
                    "Download_ProviderDescription",
                    imageName: nameof(MediaDownloadPopUpImages.MediaDownloadQuickStart_1)),
                MediaDownloadQuickStartContent.Create(
                    "Download_ToolsHeadline",
                    "Download_ToolsDescription",
                    imageName: nameof(MediaDownloadPopUpImages.MediaDownloadQuickStart_2)),
                MediaDownloadQuickStartContent.Create(
                    "Download_RunHeadline",
                    "Download_RunDescription",
                    imageName: nameof(MediaDownloadPopUpImages.MediaDownloadQuickStart_3))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                OptionKey,
                bool.FalseString,
                "Tracks whether the Media download quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "Media download: Quick-Start completed"));
        }
    }
}
