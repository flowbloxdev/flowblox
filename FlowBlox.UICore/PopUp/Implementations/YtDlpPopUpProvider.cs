using System.Globalization;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public sealed class YtDlpPopUpProvider :
        ComponentPopupProviderBase<YtDlpMediaDownloadProvider>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpYtDlpQuickStartCompleted;
        protected override string WindowTitle => Text("Window_Title");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(YtDlpMediaDownloadProvider target)
        {
            return
            [
                new ComponentPopupItem(
                    Text("Step1_Headline"),
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Text("Step1_Description"),
                        GlobalUrls.YtDlpWindowsExecutable,
                        GlobalUrls.DenoWindowsExecutableZip,
                        GlobalUrls.YtDlpReleases,
                        GlobalUrls.DenoReleases),
                    PopUpImageResourceHelper.GetImageSource(
                        MediaDownloadPopUpImages.ResourceManager,
                        nameof(MediaDownloadPopUpImages.YtDlpProviderQuickStart_1))),
                new ComponentPopupItem(
                    Text("Step2_Headline"),
                    Text("Step2_Description"),
                    PopUpImageResourceHelper.GetImageSource(
                        MediaDownloadPopUpImages.ResourceManager,
                        nameof(MediaDownloadPopUpImages.YtDlpProviderQuickStart_2))),
                new ComponentPopupItem(
                    Text("Step3_Headline"),
                    Text("Step3_Description"),
                    PopUpImageResourceHelper.GetImageSource(
                        MediaDownloadPopUpImages.ResourceManager,
                        nameof(MediaDownloadPopUpImages.YtDlpProviderQuickStart_3)))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                OptionsConstants.PopUpYtDlpQuickStartCompleted,
                bool.FalseString,
                "Tracks whether the yt-dlp quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "yt-dlp: Quick-Start completed"));
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(YtDlpPopUpTexts));
    }
}
