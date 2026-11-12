using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.WebBrowser;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Web
{
    public enum WebSelectorOutputMode
    {
        Element,
        Attribute
    }

    [Display(Name = "WebSelectorFlowBlock_DisplayName", Description = "WebSelectorFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class WebSelectorFlowBlock : WebActionFlowblockBase
    {
        [Display(Name = "WebSelectorFlowBlock_XPath", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public override string XPath { get; set; }

        [Display(Name = "WebSelectorFlowBlock_CSSSelector", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public override string CSSSelector { get; set; }

        [Display(Name = "WebSelectorFlowBlock_InnerContent", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public bool InnerContent { get; set; } = true;

        [Display(Name = "WebSelectorFlowBlock_OutputMode", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public WebSelectorOutputMode OutputMode { get; set; } = WebSelectorOutputMode.Element;

        [ActivationCondition(MemberName = nameof(OutputMode), Value = WebSelectorOutputMode.Attribute)]
        [ConditionallyRequired]
        [Display(Name = "WebSelectorFlowBlock_AttributeName", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string AttributeName { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cursor_default_click, 16, SKColors.DeepSkyBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cursor_default_click, 32, SKColors.DeepSkyBlue);


        public WebSelectorFlowBlock() : base()
        {
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(XPath));
            properties.Add(nameof(CSSSelector));
            properties.Add(nameof(OutputMode));
            if (OutputMode == WebSelectorOutputMode.Attribute)
                properties.Add(nameof(AttributeName));
            properties.Add(nameof(InnerContent));
            return properties;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(WebSelectorNotifications));
                return notificationTypes;
            }
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var webBrowserFlowBlock = AssociatedWebBrowser ?? GetPreviousFlowBlockOnPath<WebBrowserFlowBlock>(this);
                var webBrowser = webBrowserFlowBlock.InternalWebBrowser;

                WebEventSelectionMode mode;
                string selector;

                if (!TryRequireAndGetSelector(runtime, out mode, out selector))
                    return;

                var attributeName = this.Resolve(owner => owner.AttributeName);
                if (OutputMode == WebSelectorOutputMode.Attribute && string.IsNullOrWhiteSpace(attributeName))
                {
                    CreateNotification(runtime, WebSelectorNotifications.AttributeNameIsEmpty);
                    GenerateResult(runtime);
                    return;
                }

                var result = webBrowser.GetContents(selector, mode, InnerContent, OutputMode, attributeName);

                if (runtime != null)
                {
                    if (result.Success)
                    {
                        runtime.Report($"Content retrieval successfully executed for selector \"{selector}\" ({result.Contents.Count} matches).");
                        GenerateResult(runtime, result.Contents);
                    }
                    else
                    {
                        HandleFailure(runtime, result);
                    }
                }
            });
        }

        public enum WebSelectorNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Attribute name is empty")]
            AttributeNameIsEmpty
        }
    }
}
