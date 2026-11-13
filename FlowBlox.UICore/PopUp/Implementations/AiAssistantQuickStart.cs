using FlowBlox.Core.Constants;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.UICore.ViewModels;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public sealed class AiAssistantQuickStart : ComponentPopupProviderBase<AiAssistantChatViewModel>, IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpAiAssistantQuickStartCompleted;

        protected override string WindowTitle => Text("Window_Title");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(AiAssistantChatViewModel target) => CreateItems();

        public static IReadOnlyList<ComponentPopupItem> CreateItems() =>
            Enumerable.Range(1, 4)
                .Select(step => new ComponentPopupItem(
                    Text($"Step{step}_Headline"),
                    Text($"Step{step}_Description"),
                    PopUpImageResourceHelper.GetImageSource(
                        AiAssistantPopUpImages.ResourceManager,
                        $"AiAssistantQuickStart_{step}")))
                .ToList();

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "PopUp.AiAssistant.QuickStartCompleted",
                bool.FalseString,
                Text("Option_Completed_Description"),
                OptionElement.OptionType.Boolean,
                Text("Option_Completed_Name")));
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(AiAssistantPopUpTexts));
    }
}
