using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Rendering.Skia;
using UglyToad.PdfPig.Graphics.Colors;
using FlowBlox.Core.Exceptions;
using Tesseract;

namespace FlowBlox.Core.Models.FlowBlocks.IO
{
    [Display(Name = "PDF2TextFlowBlock_DisplayName",
             Description = "PDF2TextFlowBlock_Description",
             ResourceType = typeof(FlowBloxTexts))]
    public class PDF2TextFlowBlock : BaseSingleResultFlowBlock
    {
        [Required]
        [Display(Name = "PropertyNames_DataSource", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleDataSources), DisplayMember = nameof(DataObjectBase.Name))]
        public DataObjectBase DataSource { get; set; }

        [Display(Name = "PDF2TextFlowBlock_UseOcr", Description = "PDF2TextFlowBlock_UseOcr_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public bool UseOcr { get; set; }

        [ActivationCondition(MemberName = nameof(UseOcr), Value = true)]
        [Display(Name = "PDF2TextFlowBlock_OcrMode", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public PdfOcrMode OcrMode { get; set; }

        [ActivationCondition(MemberName = nameof(UseOcr), Value = true)]
        [RequiredIf(nameof(UseOcr), true)]
        [Display(Name = "PDF2TextFlowBlock_OcrLanguages", Description = "PDF2TextFlowBlock_OcrLanguages_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public string OcrLanguages { get; set; } = "deu+eng";

        [ActivationCondition(MemberName = nameof(UseOcr), Value = true)]
        [Display(Name = "PDF2TextFlowBlock_OcrDataDirectory", Description = "PDF2TextFlowBlock_OcrDataDirectory_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UISelectionButtons.FLD | UISelectionButtons.DI)]
        public string OcrDataDirectory { get; set; }

        [ActivationCondition(MemberName = nameof(UseOcr), Value = true)]
        [Range(72, 600)]
        [Display(Name = "PDF2TextFlowBlock_OcrDpi", Description = "PDF2TextFlowBlock_OcrDpi_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        public int OcrDpi { get; set; } = 300;

        [Display(Name = "PDF2TextFlowBlock_PdfTextResult", ResourceType = typeof(FlowBloxTexts), Order = 103)]
        [FlowBloxAssociation]
        public FieldElement PdfTextResult { get; set; }

        [Display(Name = "PDF2TextFlowBlock_OcrTextResult", ResourceType = typeof(FlowBloxTexts), Order = 104)]
        [FlowBloxAssociation]
        public FieldElement OcrTextResult { get; set; }

        [Display(Name = "PDF2TextFlowBlock_PageNumberResult", ResourceType = typeof(FlowBloxTexts), Order = 105)]
        [FlowBloxAssociation]
        public FieldElement PageNumberResult { get; set; }

        public override List<FieldElement> Fields => base.Fields
            .Concat(new[] { PdfTextResult, OcrTextResult, PageNumberResult }.Where(resultField => resultField != null))
            .Distinct()
            .ToList();

        public override void OnAfterCreate()
        {
            base.OnAfterCreate();
            EnsureOutputFields();
        }

        public override void OnAfterLoad()
        {
            base.OnAfterLoad();
            EnsureOutputFields();
        }

        private void EnsureOutputFields()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            if (registry == null)
                return;

            if (PdfTextResult == null)
            {
                PdfTextResult = registry.CreateField(this, fieldType: FieldTypes.Text);
                PdfTextResult.Name = "PdfText";
            }
            if (OcrTextResult == null)
            {
                OcrTextResult = registry.CreateField(this, fieldType: FieldTypes.Text);
                OcrTextResult.Name = "OcrText";
            }
            if (PageNumberResult == null)
            {
                PageNumberResult = registry.CreateField(this, fieldType: FieldTypes.Integer);
                PageNumberResult.Name = "PageNumber";
            }
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_pdf_box, 16);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_pdf_box, 32);
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Conversion;

        public override List<string> GetDisplayableProperties()
        {
            var props = base.GetDisplayableProperties();
            props.Add(nameof(DataSource));
            return props;
        }

        private IEnumerable<DataObjectBase> GetPossibleDataSources()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetManagedObjects<DataObjectBase>();
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                try
                {
                    if (DataSource == null || !DataSource.CanRead())
                        throw new InvalidOperationException("The data source is not ready for reading.");

                    var bytes = DataSource.Content ?? Array.Empty<byte>();
                    if (bytes.Length == 0)
                        throw new InvalidOperationException("The data source is empty.");

                    using var ms = new System.IO.MemoryStream(bytes, writable: false);
                    var useOcr = this.Resolve(block => block.UseOcr);
                    using var pdf = useOcr
                        ? PdfDocument.Open(ms, SkiaRenderingParsingOptions.Instance)
                        : PdfDocument.Open(ms);
                    if (useOcr)
                        pdf.AddSkiaPageFactory();

                    var results = new List<Dictionary<FieldElement, string>>(pdf.NumberOfPages);
                    TesseractEngine engine = null;
                    try
                    {
                        foreach (var page in pdf.GetPages())
                        {
                            runtime.GetCancellationToken().ThrowIfCancellationRequested();
                            var text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
                            var ocrText = string.Empty;
                            if (useOcr && (this.Resolve(block => block.OcrMode) == PdfOcrMode.AllPages || string.IsNullOrWhiteSpace(text)))
                            {
                                engine ??= CreateOcrEngine();
                                using var bitmap = pdf.GetPageAsSKBitmap(page.Number, this.Resolve(block => block.OcrDpi) / 72f, SKColors.White);
                                using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                                using var image = Pix.LoadFromMemory(png.ToArray());
                                using var recognition = engine.Process(image);
                                ocrText = recognition.GetText() ?? string.Empty;
                            }

                            var selectedText = !string.IsNullOrWhiteSpace(ocrText) ? ocrText : text;
                            var result = new Dictionary<FieldElement, string> { [ResultField] = selectedText };
                            if (PdfTextResult != null)
                                result[PdfTextResult] = text;
                            if (OcrTextResult != null)
                                result[OcrTextResult] = ocrText;
                            if (PageNumberResult != null)
                                result[PageNumberResult] = page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            results.Add(result);
                        }
                    }
                    finally
                    {
                        engine?.Dispose();
                    }
                    GenerateResult(runtime, results);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ExecutionEnvironmentPrerequisiteException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    runtime.Report(ex.ToString());
                    CreateNotification(runtime, PDF2TextNotifications.FailedToExtractText);
                    GenerateResult(runtime);
                }
            });
        }

        private TesseractEngine CreateOcrEngine()
        {
            var configuredDirectory = this.Resolve(block => block.OcrDataDirectory);
            var directory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "data", "ocr", "tessdata")
                : configuredDirectory;
            var languages = this.Resolve(block => block.OcrLanguages, required: true);
            try
            {
                foreach (var language in languages.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (language.IndexOfAny(new[] { '/', '\\' }) >= 0 || language.Contains("..", StringComparison.Ordinal))
                        throw new InvalidOperationException("OCR languages must be trained-data language names, for example deu+eng.");
                    if (!File.Exists(Path.Combine(directory, language + ".traineddata")))
                        throw new FileNotFoundException($"OCR language data '{language}.traineddata' is missing from '{directory}'.");
                }
                return new TesseractEngine(directory, languages, EngineMode.LstmOnly);
            }
            catch (Exception exception)
            {
                throw new ExecutionEnvironmentPrerequisiteException("PDF OCR", typeof(PDF2TextFlowBlock), directory, exception);
            }
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var list = base.NotificationTypes;
                list.Add(typeof(PDF2TextNotifications));
                return list;
            }
        }

        public enum PDF2TextNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Failed to extract PDF text")]
            FailedToExtractText
        }
    }
}
