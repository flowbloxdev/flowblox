using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.UICore.PopUp.Provider;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public sealed class FFmpegToolPopUpProvider :
        ComponentPopupProviderBase<FFmpegTool>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpFFmpegToolQuickStartCompleted;
        protected override string WindowTitle => MediaDownloadQuickStartContent.Text("Tool_WindowTitle");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(FFmpegTool target)
        {
            return
            [
                MediaDownloadQuickStartContent.Create(
                    "Tool_DownloadHeadline",
                    "Tool_DownloadDescription",
                    showConfiguration: false,
                    GlobalUrls.FFmpegWindowsEssentialsZip,
                    GlobalUrls.FFmpegDownloads),
                MediaDownloadQuickStartContent.Create(
                    "Tool_PathHeadline",
                    "Tool_PathDescription",
                    showConfiguration: true)
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                OptionKey,
                bool.FalseString,
                "Tracks whether the FFmpeg tools quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "FFmpeg tools: Quick-Start completed"));
        }
    }
}
