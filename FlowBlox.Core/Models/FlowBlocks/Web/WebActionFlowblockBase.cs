using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.FlowBlocks.Web.WebBrowser;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.WebBrowser
{
    [FlowBloxSpecialExplanation("WebActionFlowblockBase_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public abstract class WebActionFlowblockBase : BaseSingleResultFlowBlock
    {
        [Display(Name = "WebActionFlowblockBase_AssociatedWebBrowser", Description = "WebActionFlowblockBase_AssociatedWebBrowser_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 100)]
        [AssociatedFlowBlockResolvable()]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionFilterMethod = nameof(GetPossibleWebBrowserFlowBlocks),
            SelectionDisplayMember = nameof(Name))]
        public WebBrowserFlowBlock AssociatedWebBrowser { get; set; }

        public List<WebBrowserFlowBlock> GetPossibleWebBrowserFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<WebBrowserFlowBlock>()
                .ToList();
        }

        public abstract string CSSSelector { get; set; }

        public abstract string XPath { get; set; }

        public override ObservableCollection<BaseFlowBlock> ReferencedFlowBlocks
        {
            get
            {
                return base.ReferencedFlowBlocks;
            }
            set
            {
                UpdateReferencedWebExecutionRequirement(false);
                base.ReferencedFlowBlocks = value;
                UpdateReferencedWebExecutionRequirement(true);
            }
        }

        private void UpdateReferencedWebExecutionRequirement(bool required)
        {
            var webExecutionFlowBlock = GetPreviousFlowBlockOnPath(this, [typeof(WebBrowserFlowBlock), typeof(WebEventFlowBlock)]);
            if (webExecutionFlowBlock is BaseSingleResultFlowBlock singleResultFlowBlock && 
                singleResultFlowBlock?.ResultField != null)
            {
                SetFieldRequirement(singleResultFlowBlock.ResultField, required);
            }
        }

        protected virtual void RequireAndGetSelector(out WebEventSelectionMode mode, out string selector)
        {
            if (!string.IsNullOrEmpty(CSSSelector))
            {
                mode = WebEventSelectionMode.CssSelector;
                selector = this.Resolve(owner => owner.CSSSelector);
            }
            else if (!string.IsNullOrEmpty(XPath))
            {
                mode = WebEventSelectionMode.XPath;
                selector = this.Resolve(owner => owner.XPath);
            }
            else
            {
                throw new InvalidOperationException("Both XPath and CSSSelector are empty.");
            }
        }

        protected bool TryRequireAndGetSelector(BaseRuntime runtime, out WebEventSelectionMode mode, out string selector)
        {
            var adapter = new WebActionFlowBlockSelectorAdapter(this, runtime, HandleMissingSelector);
            return adapter.TryGetSelector(out mode, out selector);
        }

        protected virtual void HandleMissingSelector(BaseRuntime runtime)
        {
            CreateNotification(runtime, WebActionFlowBlockNotifications.SelectorIsEmpty);
            GenerateResult(runtime);
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(WebBrowserActionStatus));
                notificationTypes.Add(typeof(WebActionFlowBlockNotifications));
                return notificationTypes;
            }
        }

        protected virtual void HandleFailure(BaseRuntime runtime, WebBrowserActionResult result)
        {
            if (result.Exception != null)
                runtime.Report("A problem occurred while executing the web action.", FlowBloxLogLevel.Error, result.Exception);

            HandleFailure(runtime, result.Status, result.Exception);
        }

        protected virtual void HandleFailure(BaseRuntime runtime, Enum? enumValue, Exception exception = null)
        {
            if (enumValue != null)
                CreateNotification(runtime, enumValue, exception);
            
            GenerateResult(runtime);
        }

        public enum WebActionFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Selector is empty")]
            SelectorIsEmpty
        }
    }
}
