using FlowBlox.Core.Constants;
using Aspose.Words;
using Aspose.Words.Saving;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    public enum AsposePdfCompliance
    {
        [Display(Name = "AsposePdfCompliance_Pdf", ResourceType = typeof(FlowBloxTexts))]
        Pdf,
        [Display(Name = "AsposePdfCompliance_PdfA1b", ResourceType = typeof(FlowBloxTexts))]
        PdfA1b,
        [Display(Name = "AsposePdfCompliance_PdfA2u", ResourceType = typeof(FlowBloxTexts))]
        PdfA2u,
        [Display(Name = "AsposePdfCompliance_PdfA3u", ResourceType = typeof(FlowBloxTexts))]
        PdfA3u,
        [Display(Name = "AsposePdfCompliance_PdfA4", ResourceType = typeof(FlowBloxTexts))]
        PdfA4
    }

    [Display(Name = "AsposeWordToPdfFlowBlock_DisplayName", Description = "AsposeWordToPdfFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class AsposeWordToPdfFlowBlock : BaseSingleResultFlowBlock
    {

        private FieldElement _wordDocumentField;

        public override FieldTypes DefaultResultFieldType => FieldTypes.ByteArray;

        [Required]
        [Display(Name = "AsposeWordToPdfFlowBlock_WordDocumentField", Description = "AsposeWordToPdfFlowBlock_WordDocumentField_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleFieldElements),
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            Operations = UIOperations.Link | UIOperations.Unlink)]
        public FieldElement WordDocumentField
        {
            get => _wordDocumentField;
            set => SetRequiredInputField(ref _wordDocumentField, value);
        }

        [Display(Name = "AsposeWordToPdfFlowBlock_Compliance", Description = "AsposeWordToPdfFlowBlock_Compliance_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public AsposePdfCompliance Compliance { get; set; } = AsposePdfCompliance.Pdf;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_pdf_box, 16, SKColors.Firebrick);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_pdf_box, 32, SKColors.Firebrick);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Conversion;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(WordDocumentField));
            properties.Add(nameof(Compliance));
            return properties;
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement(
                "Aspose.Words.LicenseBase64",
                string.Empty,
                FlowBloxResourceUtil.GetLocalizedString("AsposeWordToPdfFlowBlock_Option_License_Description", typeof(FlowBloxTexts)),
                OptionElement.OptionType.Password,
                FlowBloxResourceUtil.GetLocalizedString("AsposeWordToPdfFlowBlock_Option_License_DisplayName", typeof(FlowBloxTexts))));

            base.OptionsInit(defaults);
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                ApplyConfiguredLicense();

                var documentBytes = FlowBloxBinaryContentHelper.Resolve(WordDocumentField);
                using var inputStream = new MemoryStream(documentBytes);
                var document = new Document(inputStream);

                var saveOptions = new PdfSaveOptions
                {
                    Compliance = ToAsposeCompliance(Compliance)
                };

                using var outputStream = new MemoryStream();
                document.Save(outputStream, saveOptions);
                GenerateResult(runtime, Convert.ToBase64String(outputStream.ToArray()));
            });
        }

        private static void ApplyConfiguredLicense()
        {
            var options = FlowBloxOptions.GetOptionInstance();
            if (!options.OptionCollection.TryGetValue(OptionsConstants.AsposeWordsLicenseBase64, out var licenseOption))
                return;

            var licenseBase64 = licenseOption.Value?.Trim();
            if (string.IsNullOrWhiteSpace(licenseBase64))
                return;

            byte[] licenseBytes;
            try
            {
                licenseBytes = Convert.FromBase64String(licenseBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("The configured Aspose.Words license is not valid Base64 content.", ex);
            }

            using var licenseStream = new MemoryStream(licenseBytes);
            new License().SetLicense(licenseStream);
        }

        private static PdfCompliance ToAsposeCompliance(AsposePdfCompliance compliance)
        {
            return compliance switch
            {
                AsposePdfCompliance.PdfA1b => PdfCompliance.PdfA1b,
                AsposePdfCompliance.PdfA2u => PdfCompliance.PdfA2u,
                AsposePdfCompliance.PdfA3u => PdfCompliance.PdfA3u,
                AsposePdfCompliance.PdfA4 => PdfCompliance.PdfA4,
                _ => PdfCompliance.Pdf17
            };
        }
    }
}
