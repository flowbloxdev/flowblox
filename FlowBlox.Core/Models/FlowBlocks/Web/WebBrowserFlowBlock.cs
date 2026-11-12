using FlowBlox.Core.Extensions;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Web.InternalWebBrowser;
using FlowBlox.Core.Models.FlowBlocks.WebBrowser;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Web
{
    [FlowBloxUIGroup("WebBrowserFlowBlock_Groups_Advanced", 0)]
    [Display(Name = "WebBrowserFlowBlock_DisplayName", Description = "WebBrowserFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("WebBrowserFlowBlock_SpecialExplanation_ManagedResource", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("WebBrowserFlowBlock_SpecialExplanation_ResultBehavior", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("WebBrowserFlowBlock_SpecialExplanation_CookieOverlay", Icon = SpecialExplanationIcon.Important)]
    [FlowBloxSpecialExplanation("WebBrowserFlowBlock_SpecialExplanation_BrowserConfiguration", Icon = SpecialExplanationIcon.Hint)]
    public class WebBrowserFlowBlock : BaseSingleResultFlowBlock
    {
        WebBrowserBase _internalWebBrowser;
        public WebBrowserBase InternalWebBrowser
        {
            get
            {
                if (_internalWebBrowser == null)
                {
                    var options = FlowBloxOptions.GetOptionInstance().OptionCollection;
                    if (!options.TryGetValue(OptionsConstants.WebBrowserServiceName, out var serviceNameOption) || string.IsNullOrWhiteSpace(serviceNameOption.Value))
                    {
                        throw new InvalidOperationException(
                            "Option 'WebBrowser.ServiceName' is not configured or is empty. " +
                            "Please configure a valid web browser service name.");
                    }

                    var serviceName = serviceNameOption.Value;

                    if (!options.TryGetValue(OptionsConstants.WebBrowserServiceSettings, out var serializedSettings) || string.IsNullOrWhiteSpace(serializedSettings.Value))
                    {
                        throw new InvalidOperationException(
                            "Option 'WebBrowser.ServiceSettings' is not configured or is empty. " +
                            "Please provide valid web browser settings.");
                    }

                    _internalWebBrowser = WebBrowserFactory.Create(serviceName, serializedSettings.Value);
                }

                return _internalWebBrowser;
            }
        }

        public WebBrowserFlowBlock()
        {
            RetryTimeunit = WebBrowserConstants.DefaultRetryTimeunit;
            RetryLimit = WebBrowserConstants.DefaultRetryLimit;
            Timeout = WebBrowserConstants.DefaultTimeout;
        }

        public override void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            var chromeServiceName = typeof(ChromeWebBrowser).Name;
            var firefoxServiceName = typeof(FirefoxWebBrowser).Name;

            // Determine the currently configured service name (if any)
            string configuredServiceName = null;

            var existingServiceNameOption = currentOptions
                .FirstOrDefault(o => o.Name == OptionsConstants.WebBrowserServiceName);

            if (!string.IsNullOrWhiteSpace(existingServiceNameOption?.Value))
                configuredServiceName = existingServiceNameOption.Value;

            // If nothing is configured yet, fall back to Chrome as default
            var effectiveServiceName = string.IsNullOrWhiteSpace(configuredServiceName)
                ? chromeServiceName
                : configuredServiceName;

            // Add default for WebBrowser.ServiceName (Chrome as the general default)
            defaults.Add(new OptionElement(
                "WebBrowser.ServiceName",
                chromeServiceName,
                "The default web browser implementation used for web automation.",
                OptionElement.OptionType.Text));

            // Choose default settings DTO based on the effective service name
            SeleniumWebBrowserSettingsDto defaultSettings;
            if (string.Equals(effectiveServiceName, firefoxServiceName, StringComparison.OrdinalIgnoreCase))
                defaultSettings = FirefoxWebBrowserSettingsDto.CreateDefault();
            else
            {
                // Fallback and also the normal default: Chrome
                defaultSettings = ChromeWebBrowserSettingsDto.CreateDefault();
            }

            var defaultSettingsJson = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);

            defaults.Add(new OptionElement(
                "WebBrowser.ServiceSettings",
                defaultSettingsJson,
                "JSON settings for the default web browser implementation.",
                OptionElement.OptionType.Text));

            base.OptionsInit(defaults, currentOptions);
        }

        [Display(Name = "WebBrowserFlowBlock_AssociatedWebBrowser", Description = "WebBrowserFlowBlock_AssociatedWebBrowser_Tooltip", ResourceType = typeof(FlowBloxTexts), 
            GroupName = "WebBrowserFlowBlock_Groups_Advanced", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionFilterMethod = nameof(GetPossibleWebBrowsers), 
            SelectionDisplayMember = nameof(Name))]
        public WebBrowserFlowBlock AssociatedWebBrowser { get; set; }

        [Display(Name = "WebBrowserFlowBlock_Timeout", Description = "WebBrowserFlowBlock_Timeout_Tooltip", ResourceType = typeof(FlowBloxTexts), 
            GroupName = "WebBrowserFlowBlock_Groups_Advanced", Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int Timeout { get; set; }

        [Display(Name = "WebBrowserFlowBlock_RetryLimit", Description = "WebBrowserFlowBlock_RetryLimit_Tooltip", ResourceType = typeof(FlowBloxTexts),
            GroupName = "WebBrowserFlowBlock_Groups_Advanced", Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int RetryLimit { get; set; }

        [Display(Name = "WebBrowserFlowBlock_RetryTimeunit", Description = "WebBrowserFlowBlock_RetryTimeunit_Tooltip", ResourceType = typeof(FlowBloxTexts),
            GroupName = "WebBrowserFlowBlock_Groups_Advanced", Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int RetryTimeunit { get; set; }

        public List<WebBrowserFlowBlock> GetPossibleWebBrowsers()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<WebBrowserFlowBlock>()
                .Except([this])
                .ToList();
        }

        [Display(Name = "WebBrowserFlowBlock_Url", Description = "WebBrowserFlowBlock_Url_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Url { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.web, 16, SKColors.DeepSkyBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.web, 32, SKColors.DeepSkyBlue);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Url));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                // Determine URL first; empty URL means "no navigation".
                string url = this.Resolve(owner => owner.Url);
                if (string.IsNullOrWhiteSpace(url))
                {
                    CreateNotification(runtime, WebBrowserNotifications.RequestedUrlIsEmpty);
                    GenerateResult(runtime, string.Empty);
                    return;
                }

                // Advanced configuration
                InternalWebBrowser.Timeout = Timeout;
                InternalWebBrowser.RetryLimit = RetryLimit;
                InternalWebBrowser.RetryTimeunit = RetryTimeunit;

                // Open URL
                InternalWebBrowser.Open(url);

                // In the case of an associated web browser, the cookies are set and the page is reloaded.
                if (AssociatedWebBrowser?.InternalWebBrowser != null)
                {
                    var associatedWebBrowser = AssociatedWebBrowser.InternalWebBrowser;
                    InternalWebBrowser.SetCookies(associatedWebBrowser.GetAllCookies());
                    InternalWebBrowser.Open(url);
                }

                string content = InternalWebBrowser.DOMContent;

                GenerateResult(runtime, content);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(WebBrowserNotifications));
                return notificationTypes;
            }
        }

        public enum WebBrowserNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Requested URL is empty")]
            RequestedUrlIsEmpty
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (_internalWebBrowser != null)
            {
                try
                {
                    _internalWebBrowser.Close();
                }
                catch(Exception e)
                {
                    runtime.Report("An error occurred while closing the web browser instance.", FlowBloxLogLevel.Warning, e);
                }
                
                _internalWebBrowser = null;
            }

            base.RuntimeFinished(runtime);
        }
    }
}
