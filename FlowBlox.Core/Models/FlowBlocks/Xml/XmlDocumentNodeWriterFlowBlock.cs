using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Xml;

namespace FlowBlox.Core.Models.FlowBlocks.Xml
{
    [Display(Name = "XmlDocumentNodeWriterFlowBlock_DisplayName", Description = "XmlDocumentNodeWriterFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("XmlDocumentNodeWriterFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public class XmlDocumentNodeWriterFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Display(Name = "XmlDocumentNodeWriterFlowBlock_AssociatedXmlDocument", Description = "XmlDocumentNodeWriterFlowBlock_AssociatedXmlDocument_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [AssociatedFlowBlockResolvable]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleXmlDocumentFlowBlocks), DisplayMember = nameof(Name))]
        public XmlDocumentFlowBlock AssociatedXmlDocument { get; set; }

        public List<XmlDocumentFlowBlock> GetPossibleXmlDocumentFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<XmlDocumentFlowBlock>()
                .ToList();
        }

        [Display(Name = "XmlDocumentNodeWriterFlowBlock_AssociatedNodeWriter", Description = "XmlDocumentNodeWriterFlowBlock_AssociatedNodeWriter_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleSourceNodes), DisplayMember = nameof(BaseFlowBlock.Name))]
        public XmlDocumentNodeWriterFlowBlock AssociatedNodeWriter { get; set; }

        public List<XmlDocumentNodeWriterFlowBlock> GetPossibleSourceNodes()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var flowBlocks = registry.GetFlowBlocks<XmlDocumentNodeWriterFlowBlock>();
            return flowBlocks.ToList();
        }

        [Display(Name = "XmlDocumentNodeWriterFlowBlock_XPath", Description = "XmlDocumentNodeWriterFlowBlock_XPath_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public string XPath { get; set; }


        [Display(Name = "XmlDocumentNodeWriterFlowBlock_NodeName", Description = "XmlDocumentNodeWriterFlowBlock_NodeName_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [Required]
        public string NodeName { get; set; }

        [Display(Name = "XmlDocumentNodeWriterFlowBlock_UpdateExistingNode_DisplayName", Description = "XmlDocumentNodeWriterFlowBlock_UpdateExistingNode_Tooltip", 
            ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public bool UpdateExistingNode { get; set; }

        [Display(Name = "XmlDocumentNodeWriterFlowBlock_Assignments", Description = "XmlDocumentNodeWriterFlowBlock_Assignments_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        [FlowBloxDataGrid]
        public ObservableCollection<XmlAssignment> Assignments { get; set; }

        public XmlDocumentNodeWriterFlowBlock()
        {
            UpdateExistingNode = true;
            Assignments = new ObservableCollection<XmlAssignment>();
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_tags, 16, SKColors.Sienna);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_tags, 32, SKColors.Sienna);

        protected void ApplyAssignments(XmlNode baseNode)
        {
            foreach (var assignment in Assignments)
            {
                var xpath = assignment.Resolve(owner => owner.XPath);
                var value = assignment.FieldValue != null
                    ? assignment.FieldValue.Value?.ToString()
                    : assignment.Resolve(owner => owner.Value);

                XPathEnsurer.EnsureXPathExists(baseNode, xpath);

                var target = baseNode.SelectSingleNode(xpath);

                if (target is XmlAttribute attr)
                {
                    attr.Value = value;
                }
                else if (target is XmlElement el)
                {
                    el.InnerText = value;
                }
            }
        }

        [JsonIgnore]
        [DeepCopierIgnore]
        public XmlNode CreatedOrUpdatedNode { get; private set; }

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Xml;

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedXmlDocument));
            properties.Add(nameof(AssociatedNodeWriter));
            properties.Add(nameof(XPath));
            properties.Add(nameof(Assignments));
            return properties;
        }

        private static string ExtractSingleLevelNodeName(string nodeExpression)
        {
            if (string.IsNullOrWhiteSpace(nodeExpression))
                throw new InvalidOperationException("NodeName must not be empty.");

            // Disallow multi-level XPath
            if (nodeExpression.Contains("/"))
                throw new InvalidOperationException(
                    $"Invalid NodeName '{nodeExpression}'. Only single-level node expressions are allowed.");

            // Extract element name before attribute filter
            var bracketIndex = nodeExpression.IndexOf("[");
            if (bracketIndex > 0)
                return nodeExpression.Substring(0, bracketIndex);

            return nodeExpression;
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

                var xpath = this.Resolve(owner => owner.XPath);
                var nodeName = this.Resolve(owner => owner.NodeName);

                XmlNode rootNode = null;
                XmlNode parent = null;

                if (AssociatedNodeWriter != null)
                {
                    if (AssociatedNodeWriter.CreatedOrUpdatedNode == null)
                        throw new InvalidOperationException("NodeSource was specified, but no CreatedNode exists.");

                    rootNode = AssociatedNodeWriter.CreatedOrUpdatedNode;
                    parent = string.IsNullOrEmpty(xpath)
                        ? rootNode
                        : rootNode.SelectSingleNode(xpath);
                }
                else if (associatedXmlDocument?.InternalXmlDocument != null)
                {
                    rootNode = associatedXmlDocument.InternalXmlDocument.DocumentElement;
                    parent = string.IsNullOrEmpty(xpath)
                        ? rootNode
                        : associatedXmlDocument.InternalXmlDocument.SelectSingleNode(xpath);
                }

                if (parent == null)
                {
                    if (!string.IsNullOrEmpty(xpath))
                        throw new InvalidOperationException($"The target node could not be found using the specified XPath: '{xpath}'.");
                    else
                        throw new InvalidOperationException("The target node could not be found.");
                }

                XmlNode newOrUpdatedNode = null;

                if (this.Resolve(x => x.UpdateExistingNode))
                    newOrUpdatedNode = parent.SelectSingleNode(nodeName);

                if (newOrUpdatedNode == null)
                {
                    var baseDoc = associatedXmlDocument.InternalXmlDocument;
                    var pureNodeName = ExtractSingleLevelNodeName(nodeName);
                    var newNode = baseDoc!.CreateElement(pureNodeName);
                    parent.AppendChild(newNode);
                    newOrUpdatedNode = newNode;
                }

                CreatedOrUpdatedNode = newOrUpdatedNode;
                ApplyAssignments(newOrUpdatedNode);
                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}


