using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public class ComponentValidationFailedPopUpProvider :
        ComponentPopupProviderBase<IFlowBloxComponent>,
        IOptionsRegistration
    {
        public override ComponentPopupEvent PopupEvent => ComponentPopupEvent.ValidationFailed;

        public override string OptionKey => OptionsConstants.PopUpComponentValidationGuidanceCompleted;

        protected override string WindowTitle => ComponentValidationFailedPopUpTexts.Window_Title;

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(IFlowBloxComponent target)
        {
            return
            [
                new ComponentPopupItem(
                    ComponentValidationFailedPopUpTexts.Step1_Headline,
                    ComponentValidationFailedPopUpTexts.Step1_Description,
                    PopUpImageResourceHelper.GetImageSource(
                        ComponentValidationFailedPopUpImages.ResourceManager,
                        nameof(ComponentValidationFailedPopUpImages.ComponentValidationFailed_1))),
                new ComponentPopupItem(
                    ComponentValidationFailedPopUpTexts.Step2_Headline,
                    ComponentValidationFailedPopUpTexts.Step2_Description,
                    PopUpImageResourceHelper.GetImageSource(
                        ComponentValidationFailedPopUpImages.ResourceManager,
                        nameof(ComponentValidationFailedPopUpImages.ComponentValidationFailed_2)))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "PopUp.Component.ValidationGuidanceCompleted",
                bool.FalseString,
                "Tracks whether guidance for saving without verification has been completed.",
                OptionElement.OptionType.Boolean,
                "Components: Validation guidance completed"));
        }
    }
}
