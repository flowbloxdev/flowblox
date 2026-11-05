using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public class WebBrowserPopUpProvider :
        ComponentPopupProviderBase<WebBrowserFlowBlock>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpWebBrowserQuickStartCompleted;

        protected override string WindowTitle => WebBrowserPopUpTexts.Window_Title;

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(WebBrowserFlowBlock target)
        {
            return
            [
                new ComponentPopupItem(
                    WebBrowserPopUpTexts.Step1_Headline,
                    WebBrowserPopUpTexts.Step1_Description,
                    PopUpImageResourceHelper.GetImageSource(WebBrowserPopUpImages.ResourceManager, nameof(WebBrowserPopUpImages.WebBrowserQuickStart_1))),
                new ComponentPopupItem(
                    WebBrowserPopUpTexts.Step2_Headline,
                    WebBrowserPopUpTexts.Step2_Description,
                    PopUpImageResourceHelper.GetImageSource(WebBrowserPopUpImages.ResourceManager, nameof(WebBrowserPopUpImages.WebBrowserQuickStart_2)))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "PopUp.WebBrowser.QuickStartCompleted",
                bool.FalseString,
                "Tracks whether the Web Browser quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "Web Browser: Quick-Start completed"));
        }
    }
}
