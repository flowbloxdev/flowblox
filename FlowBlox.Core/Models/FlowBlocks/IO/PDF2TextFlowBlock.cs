using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
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
                    using var pdf = PdfDocument.Open(ms);

                    var pages = new List<string>(capacity: pdf.NumberOfPages);
                    foreach (var page in pdf.GetPages())
                    {
                        var text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
                        pages.Add(text);
                    }
                    GenerateResult(runtime, pages.ToArray());
                }
                catch (Exception ex)
                {
                    runtime.Report(ex.ToString());
                    CreateNotification(runtime, PDF2TextNotifications.FailedToExtractText);
                    GenerateResult(runtime);
                }
            });
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
