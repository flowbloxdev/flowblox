using FlowBlox.Core.Util.Downloads;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Web.WebBrowser;
using FlowBlox.Core.Models.FlowBlocks.WebBrowser;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Web
{
    [FlowBloxUIGroup("WebDownloadFlowBlock_Groups_Download", 0)]
    [Display(Name = "WebDownloadFlowBlock_DisplayName", Description = "WebDownloadFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("WebDownloadFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("WebDownloadFlowBlock_SpecialExplanation_ResultBehavior", Icon = SpecialExplanationIcon.Information)]
    public class WebDownloadFlowBlock : BaseResultFlowBlock
    {
        [Display(Name = "WebDownloadFlowBlock_AssociatedWebBrowser", Description = "WebDownloadFlowBlock_AssociatedWebBrowser_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
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

        [Display(Name = "WebDownloadFlowBlock_DownloadMode", Description = "WebDownloadFlowBlock_DownloadMode_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "WebDownloadFlowBlock_Groups_Download", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public WebDownloadMode DownloadMode { get; set; } = WebDownloadMode.Auto;

        [Display(Name = "WebDownloadFlowBlock_XPath", Description = "WebDownloadFlowBlock_XPath_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Default, ToolboxCategory = nameof(FlowBloxToolboxCategory.XPath), UiOptions = UIOptions.EnableFieldSelection)]
        public string XPath { get; set; }

        [Display(Name = "WebDownloadFlowBlock_CSSSelector", Description = "WebDownloadFlowBlock_CSSSelector_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public string CSSSelector { get; set; }

        [Display(Name = "WebDownloadFlowBlock_DownloadPath", Description = "WebDownloadFlowBlock_DownloadPath_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "WebDownloadFlowBlock_Groups_Download", Order = 1)]
        [ActivationCondition(MemberName = nameof(DownloadMode), Values = new object[] { WebDownloadMode.HttpRequest, WebDownloadMode.Auto })]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox]
        public string DownloadPath { get; set; }

        [Display(Name = "WebDownloadFlowBlock_DownloadDirectory", Description = "WebDownloadFlowBlock_DownloadDirectory_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "WebDownloadFlowBlock_Groups_Download", Order = 2)]
        [ActivationCondition(MemberName = nameof(DownloadMode), Values = new object[] { WebDownloadMode.BrowserNative, WebDownloadMode.Auto })]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFolderSelection)]
        [FlowBloxTextBox]
        public string DownloadDirectory { get; set; }

        [Display(Name = "WebDownloadFlowBlock_TimeoutSeconds", Description = "WebDownloadFlowBlock_TimeoutSeconds_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "WebDownloadFlowBlock_Groups_Download", Order = 3)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection)]
        public int TimeoutSeconds { get; set; } = 120;

        [Display(Name = "WebDownloadFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<WebDownloadDestinations>> ResultFields { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.download, 16, SKColors.DodgerBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.download, 32, SKColors.DodgerBlue);
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;

        public override void OnAfterCreate()
        {
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        private void CreateDefaultResultFields()
        {
            CreateDestinationResultField(ResultFields, WebDownloadDestinations.DOMContent);
            CreateDestinationResultField(ResultFields, WebDownloadDestinations.DownloadPath);
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return ResultFields
                    .Where(x => x.EnumValue != null)
                    .Select(x => x.ResultField)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedWebBrowser));
            properties.Add(nameof(DownloadMode));
            properties.Add(nameof(XPath));
            properties.Add(nameof(CSSSelector));
            properties.Add(nameof(DownloadPath));
            properties.Add(nameof(DownloadDirectory));
            properties.Add(nameof(TimeoutSeconds));
            return properties;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(WebBrowserActionStatus));
                notificationTypes.Add(typeof(WebDownloadNotifications));
                return notificationTypes;
            }
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                var webBrowserFlowBlock = AssociatedWebBrowser ?? GetPreviousFlowBlockOnPath<WebBrowserFlowBlock>(this);
                if (webBrowserFlowBlock == null)
                    throw new InvalidOperationException("No web browser source is assigned to the flow block.");

                var webBrowser = webBrowserFlowBlock.InternalWebBrowser;
                if (!TryRequireAndGetSelector(runtime, out var mode, out var selector))
                    return;

                var downloadPath = this.Resolve(owner => owner.DownloadPath);
                var downloadDirectory = this.Resolve(owner => owner.DownloadDirectory);
                var timeoutSeconds = this.Resolve(owner => owner.TimeoutSeconds);
                if (timeoutSeconds < 0)
                    throw new ArgumentException("TimeoutSeconds must be greater than or equal to 0.", nameof(TimeoutSeconds));

                var progress = new TransferProgressReporter(message => runtime.Report(message, FlowBloxLogLevel.Info));
                WebBrowserDownloadActionResult result;
                try
                {
                    result = webBrowser.DownloadFile(
                        selector,
                        mode,
                        this.Resolve(owner => owner.DownloadMode),
                        downloadPath,
                        downloadDirectory,
                        timeoutSeconds,
                        progress,
                        runtime.GetCancellationToken());
                }
                catch (OperationCanceledException)
                {
                    throw new RuntimeCancellationException();
                }

                if (!result.Success)
                {
                    if (result.Exception != null)
                        runtime.Report("A problem occurred while downloading a file.", FlowBloxLogLevel.Error, result.Exception);

                    if (result.Status.HasValue)
                        CreateNotification(runtime, result.Status.Value);
                }
                else
                {
                    progress.Complete();
                    runtime.Report($"Download action successfully executed for selector \"{selector}\". Downloaded file: \"{result.DownloadPath}\".");
                }

                var row = new ResultFieldByEnumValueResultBuilder<WebDownloadDestinations>()
                    .For(WebDownloadDestinations.DOMContent, webBrowser.DOMContent)
                    .For(WebDownloadDestinations.DownloadPath, result.DownloadPath ?? string.Empty)
                    .Build(ResultFields);

                GenerateResult(runtime, [row]);
            });
        }

        private bool TryRequireAndGetSelector(BaseRuntime runtime, out WebEventSelectionMode mode, out string selector)
        {
            var adapter = new WebDownloadFlowBlockSelectorAdapter(this, runtime, HandleMissingSelector);
            return adapter.TryGetSelector(out mode, out selector);
        }

        private void HandleMissingSelector(BaseRuntime runtime)
        {
            CreateNotification(runtime, WebDownloadNotifications.SelectorIsEmpty);
            GenerateResult(runtime);
        }

        public enum WebDownloadNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Selector is empty")]
            SelectorIsEmpty
        }
    }
}
