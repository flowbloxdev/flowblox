using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;
using System.Globalization;
using System.IO;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public class OnnxGenAIPopUpProvider :
        ComponentPopupProviderBase<OnnxGenAIFlowBlock>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpOnnxGenAIQuickStartCompleted;

        protected override string WindowTitle => Text("Window_Title");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(OnnxGenAIFlowBlock target)
        {
            var scriptPageUri = GetScriptPageUri("download_phi4_mini_instruct_onnx.html");
            var largeScriptPageUri = GetScriptPageUri("download_large_cuda_genai_model.html");

            return
            [
                new ComponentPopupItem(
                    Text("Step1_Headline"),
                    string.Format(CultureInfo.CurrentCulture, Text("Step1_Description"), GlobalUrls.PythonDownloads),
                    PopUpImageResourceHelper.GetImageSource(
                        OnnxGenAIPopUpImages.ResourceManager,
                        nameof(OnnxGenAIPopUpImages.OnnxGenAIQuickStart_1))),
                new ComponentPopupItem(
                    Text("Step2_Headline"),
                    string.Format(CultureInfo.CurrentCulture, Text("Step2_Description"), scriptPageUri, largeScriptPageUri),
                    PopUpImageResourceHelper.GetImageSource(
                        OnnxGenAIPopUpImages.ResourceManager,
                        nameof(OnnxGenAIPopUpImages.OnnxGenAIQuickStart_2))),
                new ComponentPopupItem(
                    Text("Step3_Headline"),
                    Text("Step3_Description"),
                    PopUpImageResourceHelper.GetImageSource(
                        OnnxGenAIPopUpImages.ResourceManager,
                        nameof(OnnxGenAIPopUpImages.OnnxGenAIQuickStart_3)))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "PopUp.OnnxGenAI.QuickStartCompleted",
                bool.FalseString,
                "Tracks whether the ONNX GenAI quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "ONNX GenAI: Quick-Start completed"));
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(OnnxGenAIPopUpTexts));

        private static string GetScriptPageUri(string fileName)
        {
            var path = Path.Combine(
                GlobalPaths.CurrentDirectory,
                "data",
                "html",
                fileName);

            return new Uri(path).AbsoluteUri;
        }
    }
}
