using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.IO;

[Display(Name = "DetectDocumentFormatFlowBlock_DisplayName", Description = "DetectDocumentFormatFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class DetectDocumentFormatFlowBlock : BaseSingleResultFlowBlock
{
    private FieldElement _contentField;

    [Required]
    [Display(Name = "DetectDocumentFormatFlowBlock_ContentField", Description = "DetectDocumentFormatFlowBlock_ContentField_Tooltip", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxAssociation]
    [FlowBloxItemsSource(Method = nameof(GetPossibleFieldElements), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
    public FieldElement ContentField
    {
        get => _contentField;
        set => SetRequiredInputField(ref _contentField, value);
    }

    public override string DefaultResultFieldName => "Format";

    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_arrow_right_outline, 16, SKColors.RoyalBlue);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_document_arrow_right_outline, 32, SKColors.RoyalBlue);

    public override FlowBlockCategory GetCategory() => FlowBlockCategory.Conversion;
    public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

    public override List<string> GetDisplayableProperties()
    {
        var properties = base.GetDisplayableProperties();
        properties.Add(nameof(ContentField));
        return properties;
    }

    public override List<Type> NotificationTypes
    {
        get
        {
            var notificationTypes = base.NotificationTypes;
            notificationTypes.Add(typeof(DocumentFormatNotifications));
            return notificationTypes;
        }
    }

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            try
            {
                var bytes = FlowBloxBinaryContentHelper.Resolve(ContentField);
                GenerateResult(runtime, DetectFormat(bytes));
            }
            catch (Exception exception)
            {
                runtime.Report("Failed to detect the binary document format: " + exception);
                CreateNotification(runtime, DocumentFormatNotifications.UnsupportedOrInvalidDocument, exception);
                GenerateResult(runtime);
            }
        });
    }

    private static string DetectFormat(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith("%PDF-"u8))
            return "Pdf";

        if (bytes.AsSpan().StartsWith("PK\u0003\u0004"u8) || bytes.AsSpan().StartsWith("PK\u0005\u0006"u8))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.GetEntry("[Content_Types].xml") != null)
            {
                if (archive.GetEntry("word/document.xml") != null)
                    return "Docx";

                if (archive.GetEntry("xl/workbook.xml") != null)
                    return "Xlsx";
            }

            throw new NotSupportedException("Only DOCX and XLSX document archives are supported.");
        }

        var text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.Any(character => char.IsControl(character) && character != '\r' && character != '\n' && character != '\t' && character != '\f'))
            throw new NotSupportedException("The input is not a supported document or UTF-8 text.");

        return "Text";
    }

    public enum DocumentFormatNotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Warning)]
        [Display(Name = "Unsupported or invalid document")]
        UnsupportedOrInvalidDocument
    }
}
