using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Xml
{
    [Display(Name = "XmlDocumentXPathSelector_DisplayName", Description = "XmlDocumentXPathSelector_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("XmlDocumentXPathSelectorFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public class XmlDocumentXPathSelectorFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "XmlDocumentXPathSelector_AssociatedXmlDocument", Description = "XmlDocumentXPathSelector_AssociatedXmlDocument_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
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

        [Display(Name = "XmlDocumentXPathSelector_XPath", Description = "XmlDocumentXPathSelector_XPath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.XPath), UiOptions = UIOptions.EnableFieldSelection)]
        [Required]
        public string XPath { get; set; }

        [Display(Name = "XmlDocumentXPathSelector_ResultMode", Description = "XmlDocumentXPathSelector_ResultMode_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public XPathResultMode ResultMode { get; set; } = XPathResultMode.InnerXml;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.magnify_scan, 16, SKColors.MediumSeaGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.magnify_scan, 32, SKColors.MediumSeaGreen);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Xml;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(XmlDocumentXPathSelectorNotifications));
                return notificationTypes;
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var props = base.GetDisplayableProperties();
            props.Add(nameof(AssociatedXmlDocument));
            props.Add(nameof(XPath));
            props.Add(nameof(ResultMode));
            return props;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var associatedXmlDocument = this.AssociatedXmlDocument ?? GetPreviousFlowBlockOnPath<XmlDocumentFlowBlock>(this);
                if (associatedXmlDocument == null)
                    throw new InvalidOperationException("No XML document source is assigned.");

                var xmlDoc = associatedXmlDocument.InternalXmlDocument;
                if (xmlDoc == null)
                    throw new InvalidOperationException("The XML document in the source flow block is null or has not been initialized.");

                if (!this.ResolveOrCreateNotification(
                    owner => owner.XPath,
                    out var xPath,
                    runtime,
                    XmlDocumentXPathSelectorNotifications.XPathExpressionIsEmpty))
                    return;

                var contents = XPathSelector.SelectValues(xmlDoc, xPath, ResultMode);
                if (contents.Count == 0)
                {
                    CreateNotification(runtime, XmlDocumentXPathSelectorNotifications.NoMatchingNodesFound);
                    GenerateResult(runtime);
                    return;
                }

                GenerateResult(runtime, contents);
            });
        }

        public enum XmlDocumentXPathSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "XmlDocumentXPathSelector_Notification_XPathExpressionIsEmpty", ResourceType = typeof(FlowBloxTexts))]
            XPathExpressionIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "XmlDocumentXPathSelector_Notification_NoMatchingValuesFound", ResourceType = typeof(FlowBloxTexts))]
            NoMatchingNodesFound
        }
    }
}


