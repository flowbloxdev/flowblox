using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.UICore.PopUp.Provider;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public sealed class FFmpegMediaDownloadPopUpProvider :
        ComponentPopupProviderBase<FFmpegMediaDownloadProvider>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpFFmpegMediaDownloadQuickStartCompleted;
        protected override string WindowTitle => MediaDownloadQuickStartContent.Text("FFmpeg_WindowTitle");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(FFmpegMediaDownloadProvider target)
        {
            return
            [
                MediaDownloadQuickStartContent.Create(
                    "FFmpeg_SourceHeadline",
                    "FFmpeg_SourceDescription",
                    showConfiguration: false),
                MediaDownloadQuickStartContent.Create(
                    "FFmpeg_ToolsHeadline",
                    "FFmpeg_ToolsDescription",
                    showConfiguration: true)
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                OptionKey,
                bool.FalseString,
                "Tracks whether the FFmpeg media download quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "FFmpeg media download: Quick-Start completed"));
        }
    }
}
