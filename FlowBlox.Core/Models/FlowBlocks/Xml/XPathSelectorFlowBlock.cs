using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Xml;

namespace FlowBlox.Core.Models.FlowBlocks.Xml
{
    [Display(Name = "XPathSelectorFlowBlock_DisplayName", Description = "XPathSelectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class XPathSelectorFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "XPathSelectorFlowBlock_XmlContent", Description = "XPathSelectorFlowBlock_XmlContent_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true, SyntaxHighlighting = "XML")]
        [Required]
        public string XmlContent { get; set; }

        [Display(Name = "XPathSelectorFlowBlock_XPath", Description = "XPathSelectorFlowBlock_XPath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.XPath))]
        [Required]
        public string XPath { get; set; }

        [Display(Name = "XPathSelectorFlowBlock_ResultMode", Description = "XPathSelectorFlowBlock_ResultMode_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public XPathResultMode ResultMode { get; set; } = XPathResultMode.InnerXml;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_search, 16, SKColors.MediumSeaGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_search, 32, SKColors.MediumSeaGreen);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Xml;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(XPathSelectorNotifications));
                return notificationTypes;
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(XPath));
            properties.Add(nameof(ResultMode));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (!this.ResolveOrCreateNotification(
                    owner => owner.XmlContent,
                    out var xmlContent,
                    runtime,
                    XPathSelectorNotifications.XmlContentIsEmpty))
                    return;

                if (!this.ResolveOrCreateNotification(
                    owner => owner.XPath,
                    out var xpath,
                    runtime,
                    XPathSelectorNotifications.XPathExpressionIsEmpty))
                    return;

                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(xmlContent);

                var values = XPathSelector.SelectValues(xmlDocument, xpath, this.Resolve(x => x.ResultMode));
                if (values.Count == 0)
                    CreateNotification(runtime, XPathSelectorNotifications.NoMatchingValuesFound);

                GenerateResult(runtime, values);
            });
        }

        public enum XPathSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "XPathSelectorFlowBlock_Notification_XmlContentIsEmpty", ResourceType = typeof(FlowBloxTexts))]
            XmlContentIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "XPathSelectorFlowBlock_Notification_XPathExpressionIsEmpty", ResourceType = typeof(FlowBloxTexts))]
            XPathExpressionIsEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "XPathSelectorFlowBlock_Notification_NoMatchingValuesFound", ResourceType = typeof(FlowBloxTexts))]
            NoMatchingValuesFound
        }
    }
}
