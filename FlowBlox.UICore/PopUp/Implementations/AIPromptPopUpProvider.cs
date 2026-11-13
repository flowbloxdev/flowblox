using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public sealed class AIPromptPopUpProvider : ComponentPopupProviderBase<AIPromptFlowBlock>, IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpAIPromptQuickStartCompleted;

        protected override string WindowTitle => Text("Window_Title");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(AIPromptFlowBlock target)
        {
            return Enumerable.Range(1, 2)
                .Select(step => new ComponentPopupItem(
                    Text($"Step{step}_Headline"),
                    GetDescription(step),
                    PopUpImageResourceHelper.GetImageSource(
                        AIPromptPopUpImages.ResourceManager,
                        $"AIPromptQuickStart_{step}")))
                .ToList();
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                OptionKey,
                bool.FalseString,
                Text("Option_Completed_Description"),
                OptionElement.OptionType.Boolean,
                Text("Option_Completed_Name")));
        }

        private static string GetDescription(int step)
        {
            var description = Text($"Step{step}_Description");
            if (step == 1)
            {
                var recommendations = FlowBloxResourceUtil.GetLocalizedString(
                    "Step4_Description",
                    typeof(AiAssistantPopUpTexts));
                description += Environment.NewLine + Environment.NewLine + recommendations;
            }

            return description;
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(AIPromptPopUpTexts));
    }
}
