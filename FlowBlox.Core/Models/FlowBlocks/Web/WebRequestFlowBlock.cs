using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Selection.StartEndPatternSelector;
using FlowBlox.Core.Models.FlowBlocks.Web.InternalWebRequest;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Web
{
    [FlowBloxUIGroup("WebRequestFlowBlock_Groups_Advanced", 0)]
    [FlowBloxUIGroup("WebRequestFlowBlock_Groups_Payload", 1)]
    [FlowBloxUIGroup("WebRequestFlowBlock_Groups_Authentication", 2)]
    [Display(Name = "WebRequestFlowBlock_DisplayName", Description = "WebRequestFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("WebRequestFlowBlock_SpecialExplanation_ManagedResource", Icon = SpecialExplanationIcon.Information)]
    public class WebRequestFlowBlock : BaseResultFlowBlock
    {
        [Display(Name = "WebRequestFlowBlock_Url", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(IsCodingMode = true)]
        public string Url { get; set; }

        [Display(Name = "WebRequestFlowBlock_HTTPAction", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public HttpActions? HTTPAction { get; set; } = HttpActions.GET;

        private ResponseBodyKind _responseBodyKind;

        [Display(Name = "WebRequestFlowBlock_ResponseBodyKind", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public ResponseBodyKind ResponseBodyKind
        {
            get => _responseBodyKind;
            set
            {
                _responseBodyKind = value;

                // Desired output type depends on the response body type
                var desired = value == ResponseBodyKind.Bytes
                    ? FieldTypes.ByteArray
                    : FieldTypes.Text;

                // Adjust all ResultFields with EnumValue == Content
                foreach (var rf in ResultFields.Where(r =>
                             r != null &&
                             r.EnumValue.HasValue &&
                             r.EnumValue.Value == WebRequestDestinations.Content))
                {
                    // Null protection if ResultField/OutputType is not set
                    if (rf.ResultField?.FieldType != null)
                        rf.ResultField.FieldType.FieldType = desired;
                }

                OnPropertyChanged(nameof(ResultFields));
            }
        }

        [Display(Name = "WebRequestFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxDataGrid]
        public ObservableCollection<ResultFieldByEnumValue<WebRequestDestinations>> ResultFields { get; set; }

        [Display(Name = "WebRequestFlowBlock_AssociatedWebRequest", Description = "WebRequestFlowBlock_AssociatedWebRequest_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Advanced", Order = 0)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleWebRequests), DisplayMember = nameof(Name))]
        public WebRequestFlowBlock AssociatedWebRequest { get; set; }

        [Display(Name = "WebRequestFlowBlock_IgnoreAlreadyProcessedContents", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Advanced", Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool IgnoreAlreadyProcessedContents { get; set; }

        [Display(Name = "WebRequestFlowBlock_IgnoreAlreadyProcessedUrls", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Advanced", Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool IgnoreAlreadyProcessedUrls { get; set; }

        [Display(Name = "WebRequestFlowBlock_CacheAlreadyProcessedContents", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Advanced", Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool CacheAlreadyProcessedContents { get; set; }

        [Display(Name = "WebRequestFlowBlock_ContentCacheSize", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Advanced", Order = 4)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int ContentCacheSize { get; set; }

        [Display(Name = "WebRequestFlowBlock_WebRequestTimeout", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Advanced", Order = 5)]
        public int WebRequestTimeout { get; set; }

        [Display(Name = "WebRequestFlowBlock_Payload_ContentType", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Payload", Order = 0)]
        public string Payload_ContentType { get; set; }

        [Display(Name = "WebRequestFlowBlock_Payload", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Payload", Order = 0)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true)]
        public string Payload { get; set; }

        [Display(Name = "WebRequestFlowBlock_PostParameters", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Payload", Order = 1)]
        [FlowBloxDataGrid]
        public ObservableCollection<WebRequestParameter> PostParameters { get; set; }

        [Display(Name = "WebRequestFlowBlock_HeaderParameters", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Payload", Order = 2)]
        [FlowBloxDataGrid]
        public ObservableCollection<WebRequestParameter> HeaderParameters { get; set; }

        [Display(Name = "WebRequestFlowBlock_UserName", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Authentication", Order = 0)]
        public string UserName { get; set; }

        [Display(Name = "WebRequestFlowBlock_Password", ResourceType = typeof(FlowBloxTexts), GroupName = "WebRequestFlowBlock_Groups_Authentication", Order = 1)]
        [FlowBloxTextBox]
        public string Password { get; set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public ConfigurableWebRequest InternalWebRequest { get; private set; }

        public List<WebRequestFlowBlock> GetPossibleWebRequests()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var webRequests = registry.GetFlowBlocks<WebRequestFlowBlock>();
            return webRequests.ToList();
        }

        private string _result = string.Empty;
        private string _fileName;
        private string _url;

        public string WindowHandle { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cube_send, 16, SKColors.SeaGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.cube_send, 32, SKColors.SeaGreen);

        private const int DefaultContentCacheSize = 209715200;
        private const int DefaultWebRequestTimeout = 30000;

        public WebRequestFlowBlock() : base()
        {
            ResultFields = new ObservableCollection<ResultFieldByEnumValue<WebRequestDestinations>>();

            HeaderParameters = new ObservableCollection<WebRequestParameter>();
            PostParameters = new ObservableCollection<WebRequestParameter>();

            ContentCacheSize = DefaultContentCacheSize;
            WebRequestTimeout = DefaultWebRequestTimeout;

            ResponseBodyKind = ResponseBodyKind.Text;
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

        protected void CreateDefaultResultFields()
        {
            CreateDestinationResultField(ResultFields, WebRequestDestinations.Content);
        }

        public override void OnAfterCreate()
        {
            Payload_ContentType = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestPostContentType].Value;
            WebRequestTimeout = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestTimeout].GetValueInt();
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Url));
            properties.Add(nameof(UserName));
            properties.Add(nameof(Password));
            return properties;
        }

        private readonly HashSet<string> _alreadyProcessedContents = new HashSet<string>();
        private readonly HashSet<string> _alreadyProcessedUrls = new HashSet<string>();

        private void CheckAlreadyProcessed(BaseRuntime runtime, string[] urls, ref string result)
        {
            if (!string.IsNullOrEmpty(result))
            {
                if (IgnoreAlreadyProcessedContents)
                {
                    string contentHash = null;

                    // Try to determine a content hash from the body tag of the current HTTP content.
                    if (result.Contains("<body") && result.Contains("</body>"))
                    {
                        var bodyList = PatternSelector.SelectPatternFromContext(result, "<body", "</body>", true);
                        if (bodyList.Count == 1)
                        {
                            string bodyContent = bodyList[0];
                            contentHash = string.Format("{0:X}", bodyContent.GetHashCode());
                        }
                    }

                    // If that didn't work, try to derive a content hash from the entire page.
                    if (string.IsNullOrEmpty(contentHash))
                        contentHash = string.Format("{0:X}", result.GetHashCode());

                    // If the determination of the content hash was successful, add the content hash to the internal registry:
                    if (!string.IsNullOrEmpty(contentHash))
                    {
                        if (!_alreadyProcessedContents.Contains(contentHash))
                        {
                            _alreadyProcessedContents.Add(contentHash);
                        }
                        else
                        {
                            runtime.Report(CreateAlreadyProcessedLogMessage());
                            result = string.Empty;
                        }
                    }
                }

                // Check URL if defined and allowed:
                var validUrls = urls.ExceptNull();
                if (IgnoreAlreadyProcessedUrls && validUrls.Any())
                {
                    var alreadyProcessedUrls = validUrls.Where(x => _alreadyProcessedUrls.Contains(x));
                    if (alreadyProcessedUrls.Any())
                    {
                        runtime.Report(CreateAlreadyProcessedLogMessage(alreadyProcessedUrls.ToArray()));
                        result = string.Empty;
                    }
                    else
                    {
                        foreach(var validUrl in validUrls)
                        {
                            if (!_alreadyProcessedUrls.Contains(validUrl))
                                _alreadyProcessedUrls.Add(validUrl);
                        }
                    }
                }
            }
        }

        private string CreateAlreadyProcessedLogMessage() => "This content has already been processed. (!) Note: To disable this feature, you must disable the web request cache function.";

        private string CreateAlreadyProcessedLogMessage(string url) => CreateAlreadyProcessedLogMessage(new[] { url });

        private string CreateAlreadyProcessedLogMessage(string[] urls)
        {
            var urlsString = string.Join(", ", urls);
            var logMessage = $"Url/s has/have already been processed: {urlsString} (!) Note: To disable this feature, you must disable the URL cache function.";
            return logMessage;
        }

        private bool IsAlreadyProcessed(string url)
        {
            if (!IgnoreAlreadyProcessedUrls)
                return false;

            if (string.IsNullOrEmpty(url))
                return false;

            if (_alreadyProcessedUrls.Contains(url))
                return true;

            return false;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                _result = string.Empty;
                _fileName = string.Empty;
                _url = string.Empty;

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                if (!this.ResolveOrCreateNotification(
                    owner => owner.Url,
                    out var url,
                    runtime,
                    WebRequestNotifications.RequestedUrlIsEmpty))
                    return;

                if (!IsAlreadyProcessed(url))
                {
                    var invocationResult = InvokeWebRequest(runtime, url);

                    if (!invocationResult.Success)
                    {
                        runtime.Report($"The following content was generated by the failed web request:{Environment.NewLine}{invocationResult.Content}");
                        CreateNotification(runtime, WebRequestNotifications.WebRequestInvocationFailed);
                    }
                    else
                    {
                        _fileName = invocationResult.FileName;

                        if (invocationResult.BodyKind == ResponseBodyKind.Text)
                            _result = invocationResult.Content;
                        else
                            _result = Convert.ToBase64String(invocationResult.Bytes);

                        _url = invocationResult.UrlCalled;

                        CheckAlreadyProcessed(runtime, [url, invocationResult.UrlCalled], ref _result);
                    }
                }
                else
                {
                    runtime.Report(CreateAlreadyProcessedLogMessage(url));
                }

                var results = new ResultFieldByEnumValueResultBuilder<WebRequestDestinations>()
                    .For(WebRequestDestinations.Content, _result)
                    .For(WebRequestDestinations.FileName, _fileName)
                    .For(WebRequestDestinations.Url, _url)
                    .BuildSingleRow(ResultFields);

                GenerateResult(runtime, results);
            });
        }


        private MemoryCache _cache;

        private WebRequestInvocationResult InvokeWebRequest(BaseRuntime runtime, string url)
        {
            if (string.IsNullOrEmpty(url))
                return new WebRequestInvocationResult(false);

            if (CacheAlreadyProcessedContents)
            {
                if (_cache.TryGetValue(url, out WebRequestInvocationResult cachedResult))
                    return cachedResult;
            }

            runtime.Report("URL \"" + url + "\" was generated by Extractor \"" + Name + "\"", FlowBloxLogLevel.Info);

            ConfigurableWebRequest webRequest = null;
            if (AssociatedWebRequest != null)
                webRequest = AssociatedWebRequest.InternalWebRequest;

            if (webRequest == null)
                webRequest = new ConfigurableWebRequest();

            InternalWebRequest = webRequest;

            webRequest.Url = url;
            webRequest.HttpAction = this.Resolve(x => x.HTTPAction)?.ToString();
            webRequest.Timeout = this.Resolve(x => x.WebRequestTimeout);
            webRequest.UserAgent = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestUserAgent].Value;
            webRequest.Accept = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestAccept].Value;
            webRequest.AcceptEncoding = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestAcceptEncoding].Value;
            webRequest.KeepAlive = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestKeepAlive].GetValueBoolean();
            webRequest.ExpectContinue = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestExpectContinue].GetValueBoolean();
            webRequest.Version = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.WebRequestVersion].Value;
            webRequest.ResponseBodyKind = this.Resolve(x => x.ResponseBodyKind);
            webRequest.UserName = this.Resolve(owner => owner.UserName);
            webRequest.Password = this.Resolve(owner => owner.Password);
            webRequest.ContentType = this.Resolve(owner => owner.Payload_ContentType);
            webRequest.Headers = FlowBloxFieldHelper.ReplaceFieldsInDictionary(HeaderParameters.ToDictionary(x => x.Key, y => y.Value));
            webRequest.Payload = new WebRequestPayloadCreator(this).GetPayload(runtime, PostParameters.ToDictionary(x => x.Key, y => y.Value));

            var webRequestResult = webRequest.Invoke(webRequest);

            if (url != webRequestResult.UrlCalled)
                runtime.Report("URL \"" + url + "\" was redirected to URL \"" + webRequestResult.UrlCalled + "\"", FlowBloxLogLevel.Info);

            var webRequestInvocationResult = new WebRequestInvocationResult(webRequestResult);

            var cacheEntryOptions = new MemoryCacheEntryOptions();
            cacheEntryOptions.SetSize(1);
            _cache.Set(url, webRequestResult, cacheEntryOptions);
            // TODO: A null reference exception occurs at runtime.
            return webRequestInvocationResult;
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = ContentCacheSize
            });

            base.RuntimeStarted(runtime);
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (_cache != null)
            {
                _cache.Clear();
                _cache = null;
            }

            if (InternalWebRequest != null)
            {
                InternalWebRequest.Dispose();
                InternalWebRequest = null;
            }

            _alreadyProcessedContents.Clear();
            _alreadyProcessedUrls.Clear();
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.AddRange([
                new OptionElement("WebRequest.Timeout", "30000", "How long should an HTTPWebRequest be allowed to execute? (specified in milliseconds)", OptionElement.OptionType.Integer),
                new OptionElement("WebRequest.Encoding", "Default", "Possible encodings: Default, ASCII, or UTF8", OptionElement.OptionType.Text),
                new OptionElement("WebRequest.Accept", "*/*", "What content should the HTTPWebRequest accept?", OptionElement.OptionType.Text),
                new OptionElement("WebRequest.AcceptEncoding", "gzip, deflate, br", "Specifies the accepted content encoding for the HTTP request (e.g., gzip, deflate, br).", OptionElement.OptionType.Text),
                new OptionElement("WebRequest.UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0", "The UserAgent of the HTTPWebRequest.", OptionElement.OptionType.Text),
                new OptionElement("WebRequest.KeepAlive", "true", "Should the internal HTTPRequest establish a permanent connection to the remote host?", OptionElement.OptionType.Boolean, "Keep Alive"),
                new OptionElement("WebRequest.ExpectContinue", "true", "Should the HTTPRequest include an Expect: 100-Continue header when making a request?", OptionElement.OptionType.Boolean, "Expect Continue"),
                new OptionElement("WebRequest.Version", "2.0", "Specifies the HTTP protocol version to be used for the request (e.g., 1.0, 1.1, 2.0).", OptionElement.OptionType.Text),
                new OptionElement("WebRequest.PostContentType", "application/x-www-form-urlencoded", "In case of an HTTP/POST request, this value will be passed for the ContentType.", OptionElement.OptionType.Text),
            ]);

            base.OptionsInit(defaults);
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(WebRequestNotifications));
                return notificationTypes;
            }
        }

        public enum WebRequestNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Web request invocation failed")]
            WebRequestInvocationFailed,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Requested URL is empty")]
            RequestedUrlIsEmpty
        }
    }
}
