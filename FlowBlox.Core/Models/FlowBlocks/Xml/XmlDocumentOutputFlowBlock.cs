using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Xml;

namespace FlowBlox.Core.Models.FlowBlocks.Xml
{
    [Display(Name = "XmlDocumentOutputFlowBlock_DisplayName", Description = "XmlDocumentOutputFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("XmlDocumentOutputFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public class XmlDocumentOutputFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "XmlDocumentOutputFlowBlock_AssociatedXmlDocument", Description = "XmlDocumentOutputFlowBlock_AssociatedXmlDocument_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [AssociatedFlowBlockResolvable()]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionFilterMethod = nameof(GetPossibleXmlDocumentFlowBlocks),
            SelectionDisplayMember = nameof(Name))]
        public XmlDocumentFlowBlock AssociatedXmlDocument { get; set; }

        private List<XmlDocumentFlowBlock> GetPossibleXmlDocumentFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<XmlDocumentFlowBlock>()
                .ToList();
        }

        [Required]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public DotNetEncodingNames EncodingName { get; set; }

        [Display(Name = "XmlDocumentOutputFlowBlock_Indent", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool Indent { get; set; }

        [Display(Name = "XmlDocumentOutputFlowBlock_NewLineOnAttributes", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool NewLineOnAttributes { get; set; }

        [Display(Name = "XmlDocumentOutputFlowBlock_OmitXmlDeclaration", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool OmitXmlDeclaration { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_braces, 16, SKColors.Peru);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_braces, 32, SKColors.Peru);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Xml;

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public XmlDocumentOutputFlowBlock()
        {
            Indent = true;
            NewLineOnAttributes = false;
            OmitXmlDeclaration = false;
            EncodingName = DotNetEncodingNames.Default;
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedXmlDocument));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var associatedXmlDocument = this.AssociatedXmlDocument ?? GetPreviousFlowBlockOnPath<XmlDocumentFlowBlock>(this);
                if (associatedXmlDocument == null)
                    throw new InvalidOperationException("No XML document source is assigned to the flow block.");

                if (associatedXmlDocument.InternalXmlDocument == null)
                    throw new InvalidOperationException("The XML document in the source flow block is null or has not been initialized.");

                var xmlDocument = associatedXmlDocument.InternalXmlDocument;

                var settings = new XmlWriterSettings
                {
                    Indent = Indent,
                    NewLineOnAttributes = NewLineOnAttributes,
                    OmitXmlDeclaration = OmitXmlDeclaration,
                    Encoding = EncodingName.ToEncoding()
                };

                if (this.ResultField?.FieldType?.FieldType == FieldTypes.ByteArray)
                {
                    using (var memoryStream = new MemoryStream())
                    using (var writer = XmlWriter.Create(memoryStream, settings))
                    {
                        xmlDocument.Save(writer);
                        writer.Flush();
                        GenerateResult(runtime, Convert.ToBase64String(memoryStream.ToArray()));
                    }
                }
                else
                {
                    using (var stringWriter = new StringWriter())
                    using (var writer = XmlWriter.Create(stringWriter, settings))
                    {
                        xmlDocument.Save(writer);
                        writer.Flush();
                        GenerateResult(runtime, stringWriter.ToString());
                    }
                }
            });
        }
    }
}
