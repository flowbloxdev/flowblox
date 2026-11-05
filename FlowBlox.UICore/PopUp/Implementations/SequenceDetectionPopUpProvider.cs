using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public class SequenceDetectionPopUpProvider :
        ComponentPopupProviderBase<SequenceDetectionFlowBlock>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpSequenceDetectionQuickStartCompleted;

        protected override string WindowTitle => SequenceDetectionPopUpTexts.Window_Title;

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(SequenceDetectionFlowBlock target)
        {
            return
            [
                new ComponentPopupItem(
                    SequenceDetectionPopUpTexts.Step1_Headline,
                    SequenceDetectionPopUpTexts.Step1_Description,
                    PopUpImageResourceHelper.GetImageSource(SequenceDetectionPopUpImages.ResourceManager, nameof(SequenceDetectionPopUpImages.SequenceDetectionQuickStart_1))),
                new ComponentPopupItem(
                    SequenceDetectionPopUpTexts.Step2_Headline,
                    SequenceDetectionPopUpTexts.Step2_Description,
                    PopUpImageResourceHelper.GetImageSource(SequenceDetectionPopUpImages.ResourceManager, nameof(SequenceDetectionPopUpImages.SequenceDetectionQuickStart_2))),
                new ComponentPopupItem(
                    SequenceDetectionPopUpTexts.Step3_Headline,
                    SequenceDetectionPopUpTexts.Step3_Description,
                    PopUpImageResourceHelper.GetImageSource(SequenceDetectionPopUpImages.ResourceManager, nameof(SequenceDetectionPopUpImages.SequenceDetectionQuickStart_3)))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "PopUp.SequenceDetection.QuickStartCompleted",
                bool.FalseString,
                "Tracks whether the Sequence Detection quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "Sequence Detection: Quick-Start completed"));
        }
    }
}
