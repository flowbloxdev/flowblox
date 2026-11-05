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
    public class OnnxQAPopUpProvider :
        ComponentPopupProviderBase<OnnxQAFlowBlock>,
        IOptionsRegistration
    {
        public override string OptionKey => OptionsConstants.PopUpOnnxQAQuickStartCompleted;

        protected override string WindowTitle => Text("Window_Title");

        protected override IReadOnlyList<ComponentPopupItem> CreateItems(OnnxQAFlowBlock target)
        {
            var scriptPageUri = GetScriptPageUri();

            return
            [
                new ComponentPopupItem(
                    Text("Step1_Headline"),
                    string.Format(CultureInfo.CurrentCulture, Text("Step1_Description"), GlobalUrls.PythonDownloads),
                    PopUpImageResourceHelper.GetImageSource(
                        OnnxQAPopUpImages.ResourceManager,
                        nameof(OnnxQAPopUpImages.OnnxQAQuickStart_1))),
                new ComponentPopupItem(
                    Text("Step2_Headline"),
                    string.Format(CultureInfo.CurrentCulture, Text("Step2_Description"), scriptPageUri),
                    PopUpImageResourceHelper.GetImageSource(
                        OnnxQAPopUpImages.ResourceManager,
                        nameof(OnnxQAPopUpImages.OnnxQAQuickStart_2))),
                new ComponentPopupItem(
                    Text("Step3_Headline"),
                    Text("Step3_Description"),
                    PopUpImageResourceHelper.GetImageSource(
                        OnnxQAPopUpImages.ResourceManager,
                        nameof(OnnxQAPopUpImages.OnnxQAQuickStart_3)))
            ];
        }

        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            defaults.Add(new OptionElement(
                "PopUp.OnnxQA.QuickStartCompleted",
                bool.FalseString,
                "Tracks whether the ONNX QA quick-start guidance has been completed.",
                OptionElement.OptionType.Boolean,
                "ONNX QA: Quick-Start completed"));
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(OnnxQAPopUpTexts));

        private static string GetScriptPageUri()
        {
            var path = Path.Combine(
                GlobalPaths.CurrentDirectory,
                "data",
                "html",
                "export_onnx_qa_model.html");

            return new Uri(path).AbsoluteUri;
        }
    }
}
