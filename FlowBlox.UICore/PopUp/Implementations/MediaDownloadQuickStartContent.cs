using System.Globalization;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;

namespace FlowBlox.UICore.PopUp.Implementations
{
    internal static class MediaDownloadQuickStartContent
    {
        internal static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(MediaDownloadPopUpTexts));

        internal static ComponentPopupItem Create(
            string headlineKey,
            string descriptionKey,
            bool showConfiguration,
            params object[] formatArguments)
        {
            var description = string.Format(
                CultureInfo.CurrentCulture,
                Text(descriptionKey),
                formatArguments);
            var imageName = showConfiguration
                ? nameof(YtDlpPopUpImages.YtDlpQuickStart_2)
                : nameof(YtDlpPopUpImages.YtDlpQuickStart_3);

            return new ComponentPopupItem(
                Text(headlineKey),
                description,
                PopUpImageResourceHelper.GetImageSource(YtDlpPopUpImages.ResourceManager, imageName));
        }
    }
}
