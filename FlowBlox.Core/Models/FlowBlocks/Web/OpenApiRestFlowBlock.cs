using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Web.OpenApi;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;

namespace FlowBlox.Core.Models.FlowBlocks.Web
{
    [FlowBloxUIGroup("OpenApiRestFlowBlock_Groups_Parameters", 0)]
    [FlowBloxUIGroup("OpenApiRestFlowBlock_Groups_Payload", 1)]
    [FlowBloxUIGroup("OpenApiRestFlowBlock_Groups_Authentication", 2)]
    [FlowBloxUIGroup("OpenApiRestFlowBlock_Groups_Pagination", 3)]
    [Display(Name = "OpenApiRestFlowBlock_DisplayName", Description = "OpenApiRestFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class OpenApiRestFlowBlock : BaseResultFlowBlock
    {
        private string _openApiSource;
        private string _endpoint;
        private IReadOnlyList<OpenApiEndpointDescriptor> _endpoints = [];
        private bool _definitionLoadAttempted;
        private bool _isLoadingDefinition;

        public OpenApiRestFlowBlock()
        {
            EndpointSuggestions = [];
            HeaderParameters = [];
            RequestParameters = [];
            ResultFields = [];
        }

        [Required]
        [Display(Name = "OpenApiRestFlowBlock_OpenApiSource", Description = "OpenApiRestFlowBlock_OpenApiSource_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFileSelection)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true, SyntaxHighlighting = "JSON")]
        public string OpenApiSource
        {
            get => _openApiSource;
            set
            {
                _openApiSource = value;
                LoadDefinition(forceRefresh: false, clearSelection: true);
                OnPropertyChanged();
            }
        }

        [Required]
        [Display(Name = "OpenApiRestFlowBlock_Endpoint", Description = "OpenApiRestFlowBlock_Endpoint_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, SelectionFilterMethod = nameof(GetEndpointSuggestions),
            SelectionFilterDependency = nameof(EndpointSuggestions))]
        [FlowBloxComboBox(DropDownMode = DropDownMode.SelectionOnly)]
        public string Endpoint
        {
            get => _endpoint;
            set
            {
                _endpoint = value;
                ApplyEndpointDefaults();
                OnPropertyChanged();
            }
        }

        [Display(Name = "OpenApiRestFlowBlock_BaseUrl", Description = "OpenApiRestFlowBlock_BaseUrl_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string BaseUrl { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_EndpointDescription", Description = "OpenApiRestFlowBlock_EndpointDescription_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(ReadOnly = true)]
        [FlowBloxTextBox(MultiLine = true)]
        public string EndpointDescription { get; private set; }

        [Display(Name = "OpenApiRestFlowBlock_DefinitionError", Description = "OpenApiRestFlowBlock_DefinitionError_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(ReadOnly = true)]
        [FlowBloxTextBox(MultiLine = true)]
        public string DefinitionError { get; private set; }

        [Display(Name = "OpenApiRestFlowBlock_DefinitionWarnings", Description = "OpenApiRestFlowBlock_DefinitionWarnings_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(ReadOnly = true)]
        [FlowBloxTextBox(MultiLine = true)]
        public string DefinitionWarnings { get; private set; }

        [Display(Name = "OpenApiRestFlowBlock_BearerToken", Description = "OpenApiRestFlowBlock_BearerToken_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string BearerToken { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_UserName", Description = "OpenApiRestFlowBlock_UserName_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Authentication", Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string UserName { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_Password", Description = "OpenApiRestFlowBlock_Password_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Authentication", Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Password { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_FailOnHttpError", Description = "OpenApiRestFlowBlock_FailOnHttpError_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        public bool FailOnHttpError { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_Paginate", Description = "OpenApiRestFlowBlock_Paginate_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Pagination", Order = 0)]
        public bool Paginate { get; set; }

        [ActivationCondition(MemberName = nameof(Paginate), Value = true)]
        [Display(Name = "OpenApiRestFlowBlock_PaginationItemsPath", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Pagination", Order = 1)]
        public string PaginationItemsPath { get; set; } = "items";

        [ActivationCondition(MemberName = nameof(Paginate), Value = true)]
        [Display(Name = "OpenApiRestFlowBlock_PaginationOffsetParameter", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Pagination", Order = 2)]
        public string PaginationOffsetParameter { get; set; } = "startAt";

        [ActivationCondition(MemberName = nameof(Paginate), Value = true)]
        [Display(Name = "OpenApiRestFlowBlock_PaginationTotalPath", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Pagination", Order = 3)]
        public string PaginationTotalPath { get; set; } = "total";

        [ActivationCondition(MemberName = nameof(Paginate), Value = true)]
        [Range(1, 10000)]
        [Display(Name = "OpenApiRestFlowBlock_MaximumPages", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Pagination", Order = 4)]
        public int MaximumPages { get; set; } = 1000;

        [Display(Name = "OpenApiRestFlowBlock_HeaderParameters", Description = "OpenApiRestFlowBlock_HeaderParameters_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Parameters", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.GridView, Operations = UIOperations.None)]
        [FlowBloxDataGrid(GridColumnMemberNames = [nameof(OpenApiParameterValue.Name), nameof(OpenApiParameterValue.Required), nameof(OpenApiParameterValue.Description), nameof(OpenApiParameterValue.Value)])]
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<OpenApiParameterValue> HeaderParameters { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_RequestParameters", Description = "OpenApiRestFlowBlock_RequestParameters_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Parameters", Order = 1)]
        [FlowBloxUI(Factory = UIFactory.GridView, Operations = UIOperations.None)]
        [FlowBloxDataGrid(GridColumnMemberNames = [nameof(OpenApiParameterValue.Name), nameof(OpenApiParameterValue.Location), nameof(OpenApiParameterValue.Required), nameof(OpenApiParameterValue.Description), nameof(OpenApiParameterValue.Value)])]
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<OpenApiParameterValue> RequestParameters { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_ContentType", Description = "OpenApiRestFlowBlock_ContentType_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Payload", Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string ContentType { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_Payload", Description = "OpenApiRestFlowBlock_Payload_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "OpenApiRestFlowBlock_Groups_Payload", Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true, SyntaxHighlighting = "JSON")]
        public string Payload { get; set; }

        [Display(Name = "OpenApiRestFlowBlock_ResultFields", Description = "OpenApiRestFlowBlock_ResultFields_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 10)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<OpenApiRestDestinations>> ResultFields { get; set; }

        [JsonIgnore]
        public ObservableCollection<string> EndpointSuggestions { get; }

        public IEnumerable<string> GetEndpointSuggestions()
        {
            EnsureDefinitionLoaded();
            return EndpointSuggestions;
        }

        public void ReloadDefinition(bool forceRefresh, bool preserveSelection = false)
        {
            var error = LoadDefinition(forceRefresh, clearSelection: !preserveSelection);
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
        }

        private void EnsureDefinitionLoaded()
        {
            if (!_definitionLoadAttempted && !_isLoadingDefinition && !string.IsNullOrWhiteSpace(_openApiSource))
                LoadDefinition(forceRefresh: false, clearSelection: false);
        }

        private Exception? LoadDefinition(bool forceRefresh, bool clearSelection)
        {
            if (_isLoadingDefinition)
                return null;

            // Collection/property notifications can synchronously ask for suggestions again.
            // Record the attempt before notifying; failures and valid empty documents must
            // wait for an explicit reload or source change instead of retrying during rendering.
            _isLoadingDefinition = true;
            _definitionLoadAttempted = true;
            Exception? error = null;
            try
            {
                EndpointSuggestions.Clear();
                _endpoints = [];
                DefinitionError = string.Empty;
                DefinitionWarnings = string.Empty;
                if (clearSelection)
                {
                    _endpoint = string.Empty;
                    ClearEndpointDefaults();
                }
                if (!string.IsNullOrWhiteSpace(_openApiSource))
                {
                    var definition = OpenApiDefinitionLoader.Load(_openApiSource, forceRefresh);
                    _endpoints = definition.Endpoints;
                    DefinitionWarnings = definition.Warnings;
                    foreach (var endpoint in _endpoints)
                        EndpointSuggestions.Add(endpoint.DisplayName);
                }
            }
            catch (Exception ex)
            {
                _endpoints = [];
                DefinitionError = ex.Message;
                error = ex;
            }
            finally
            {
                _isLoadingDefinition = false;
            }
            OnPropertyChanged(nameof(EndpointSuggestions));
            OnPropertyChanged(nameof(DefinitionError));
            OnPropertyChanged(nameof(DefinitionWarnings));
            if (clearSelection)
                OnPropertyChanged(nameof(Endpoint));
            return error;
        }

        private void ApplyEndpointDefaults()
        {
            ClearEndpointDefaults(clearBaseUrl: false);
            EnsureDefinitionLoaded();
            var descriptor = FindEndpoint();
            if (descriptor == null) return;
            if (string.IsNullOrWhiteSpace(BaseUrl))
                BaseUrl = descriptor.ServerUrl;
            EndpointDescription = descriptor.Summary;
            ContentType = descriptor.ContentType;
            Payload = descriptor.PayloadExample;
            foreach (var parameter in descriptor.Parameters)
            {
                var copy = new OpenApiParameterValue
                {
                    Name = parameter.Name,
                    Location = parameter.Location,
                    Required = parameter.Required,
                    Description = parameter.Description,
                    Value = parameter.Value
                };
                if (string.Equals(copy.Location, "Header", StringComparison.OrdinalIgnoreCase))
                    HeaderParameters.Add(copy);
                else RequestParameters.Add(copy);
            }
            NotifyEndpointPropertiesChanged();
        }

        private void ClearEndpointDefaults(bool clearBaseUrl = true)
        {
            if (clearBaseUrl)
                BaseUrl = string.Empty;
            EndpointDescription = string.Empty;
            ContentType = string.Empty;
            Payload = string.Empty;
            HeaderParameters?.Clear();
            RequestParameters?.Clear();
            NotifyEndpointPropertiesChanged();
        }

        private void NotifyEndpointPropertiesChanged()
        {
            OnPropertyChanged(nameof(BaseUrl)); OnPropertyChanged(nameof(EndpointDescription));
            OnPropertyChanged(nameof(ContentType)); OnPropertyChanged(nameof(Payload));
            OnPropertyChanged(nameof(HeaderParameters)); OnPropertyChanged(nameof(RequestParameters));
        }

        private OpenApiEndpointDescriptor FindEndpoint()
            => _endpoints.FirstOrDefault(x => string.Equals(x.DisplayName, _endpoint, StringComparison.Ordinal));

        public override void OnAfterCreate()
        {
            CreateDestinationResultField(ResultFields, OpenApiRestDestinations.Payload);
            CreateDestinationResultField(ResultFields, OpenApiRestDestinations.StatusCode, FieldTypes.Integer);
            base.OnAfterCreate();
        }

        public override List<FieldElement> Fields => ResultFields.Where(x => x.EnumValue != null).Select(x => x.ResultField).ExceptNull().ToList();
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.web, 16, SKColors.SeaGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.web, 32, SKColors.SeaGreen);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Web;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Endpoint));
            properties.Add(nameof(BaseUrl));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
        {
            EnsureDefinitionLoaded();
            InvocationResult result;
            try
            {
                var descriptor = FindEndpoint() ?? throw new ValidationException("The selected OpenAPI endpoint is unavailable.");
                result = InvokeWithPagination(descriptor, runtime).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ValidationException or FormatException or OverflowException)
            {
                CreateNotification(runtime, OpenApiRestNotifications.InvalidResponseOrConfiguration, ex);
                runtime.Aborted = true;
                GenerateResult(runtime);
                return;
            }
            if ((FailOnHttpError || Paginate) && (result.StatusCode < 200 || result.StatusCode >= 300))
            {
                CreateNotification(runtime, OpenApiRestNotifications.RequestFailed);
                runtime.Report($"OpenAPI request failed with HTTP status {result.StatusCode}.", FlowBloxLogLevel.Error);
                runtime.Aborted = true;
                GenerateResult(runtime);
                return;
            }
            var values = new ResultFieldByEnumValueResultBuilder<OpenApiRestDestinations>()
                .For(OpenApiRestDestinations.Payload, result.Payload)
                .For(OpenApiRestDestinations.StatusCode, result.StatusCode.ToString())
                .For(OpenApiRestDestinations.Status, result.Status)
                .For(OpenApiRestDestinations.ErrorMessage, result.ErrorMessage)
                .For(OpenApiRestDestinations.ResponseHeaders, result.ResponseHeaders)
                .For(OpenApiRestDestinations.Url, result.Url)
                .BuildSingleRow(ResultFields);
            GenerateResult(runtime, values);
        });

        public override List<Type> NotificationTypes => base.NotificationTypes.Concat([typeof(OpenApiRestNotifications)]).ToList();

        public enum OpenApiRestNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "OpenApiRestFlowBlock_RequestFailed", ResourceType = typeof(FlowBloxTexts))]
            RequestFailed,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "OpenApiRestFlowBlock_InvalidResponseOrConfiguration", ResourceType = typeof(FlowBloxTexts))]
            InvalidResponseOrConfiguration
        }

        private async Task<InvocationResult> InvokeWithPagination(OpenApiEndpointDescriptor descriptor, BaseRuntime runtime)
        {
            if (!Paginate)
                return await InvokeEndpoint(descriptor, runtime).ConfigureAwait(false);

            if (descriptor.Method != "GET" || string.IsNullOrWhiteSpace(PaginationItemsPath)
                || string.IsNullOrWhiteSpace(PaginationOffsetParameter) || MaximumPages < 1)
                throw new ValidationException("Offset pagination requires a GET endpoint, an item path, an offset parameter and a positive page limit.");

            var offsetParameter = RequestParameters.SingleOrDefault(x => x.Name == PaginationOffsetParameter);
            if (offsetParameter == null || !string.Equals(offsetParameter.Location, "Query", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("The pagination offset must be a configured query parameter.");
            var offsetText = offsetParameter.Resolve(owner => owner.Value);
            var offset = string.IsNullOrWhiteSpace(offsetText) ? 0 : int.Parse(offsetText, System.Globalization.CultureInfo.InvariantCulture);
            var combined = new JArray();
            JObject firstPage = null;
            for (var pageNumber = 0; pageNumber < MaximumPages; pageNumber++)
            {
                runtime.GetCancellationToken().ThrowIfCancellationRequested();
                var response = await InvokeEndpoint(descriptor, runtime, offset).ConfigureAwait(false);
                if (response.StatusCode < 200 || response.StatusCode >= 300)
                    return response;
                // Combining pages must not reinterpret JSON string values as local dates.
                using var reader = new JsonTextReader(new StringReader(response.Payload))
                {
                    DateParseHandling = DateParseHandling.None
                };
                var page = JObject.Load(reader);
                firstPage ??= page;
                var items = page.SelectToken(PaginationItemsPath) as JArray
                    ?? throw new ValidationException("The pagination item path must select a JSON array.");
                var total = string.IsNullOrWhiteSpace(PaginationTotalPath)
                    ? null
                    : page.SelectToken(PaginationTotalPath)?.Value<int?>();
                foreach (var item in items)
                    combined.Add(item.DeepClone());
                offset = checked(offset + items.Count);
                if (total.HasValue && offset >= total.Value || !total.HasValue && items.Count == 0)
                {
                    firstPage.SelectToken(PaginationItemsPath).Replace(combined);
                    return response with { Payload = firstPage.ToString(Formatting.None) };
                }
                if (items.Count == 0)
                    throw new ValidationException("Pagination returned an empty page before the reported total was reached.");
            }
            throw new ValidationException("The pagination page limit was reached; incomplete results were discarded.");
        }

        private async Task<InvocationResult> InvokeEndpoint(OpenApiEndpointDescriptor descriptor, BaseRuntime runtime, int? offset = null)
        {
            var baseUrl = (BaseUrl == null
                ? FlowBloxFieldHelper.ReplaceFieldsInString(descriptor.ServerUrl) ?? string.Empty
                : this.Resolve(x => x.BaseUrl)).Trim();
            var path = descriptor.Path;
            var query = new List<string>();
            var cookies = new List<string>();
            foreach (var parameter in RequestParameters)
            {
                var value = parameter.Resolve(owner => owner.Value);
                if (offset.HasValue && parameter.Name == PaginationOffsetParameter)
                    value = offset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (parameter.Required && string.IsNullOrWhiteSpace(value))
                    throw new ValidationException($"Required OpenAPI parameter '{parameter.Name}' is empty.");

                if (string.IsNullOrEmpty(value))
                    continue;

                if (string.Equals(parameter.Location, "Path", StringComparison.OrdinalIgnoreCase))
                    path = path.Replace("{" + parameter.Name + "}", Uri.EscapeDataString(value), StringComparison.OrdinalIgnoreCase);
                else if (string.Equals(parameter.Location, "Cookie", StringComparison.OrdinalIgnoreCase))
                    cookies.Add($"{parameter.Name}={value}");
                else query.Add($"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(value)}");
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(path, @"\{[^{}]+\}"))
                throw new ValidationException("One or more OpenAPI path parameters are unresolved.");
            var url = UriHelper.NormalizePathSeparators(UriHelper.ConcatUri(baseUrl, path));
            if (query.Count > 0)
                url += (url.Contains('?') ? "&" : "?") + string.Join("&", query);

            try
            {
                using var client = new HttpClient();
                using var request = new HttpRequestMessage(new HttpMethod(descriptor.Method), url);
                var token = this.Resolve(owner => owner.BearerToken).Trim();
                var userName = this.Resolve(owner => owner.UserName);
                if (!string.IsNullOrWhiteSpace(userName))
                {
                    if (!string.IsNullOrWhiteSpace(token))
                        throw new ValidationException("Configure either Basic authentication or a Bearer token.");
                    var password = this.Resolve(owner => owner.Password);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(userName + ":" + password)));
                }
                if (token?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true) token = token[7..].Trim();
                if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                foreach (var parameter in HeaderParameters)
                {
                    var value = parameter.Resolve(owner => owner.Value);
                    if (parameter.Required && string.IsNullOrWhiteSpace(value))
                        throw new ValidationException($"Required OpenAPI header '{parameter.Name}' is empty.");
                    if (!string.IsNullOrEmpty(value)) request.Headers.TryAddWithoutValidation(parameter.Name, value);
                }
                if (cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
                var payload = this.Resolve(owner => owner.Payload);
                var contentType = this.Resolve(owner => owner.ContentType);
                if (!string.IsNullOrEmpty(payload))
                    request.Content = new StringContent(payload, Encoding.UTF8, string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType);
                runtime.Report($"Calling OpenAPI endpoint: {descriptor.Method} {url}", FlowBloxLogLevel.Info);
                using var response = await client.SendAsync(request, runtime.GetCancellationToken()).ConfigureAwait(false);
                var responsePayload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var headers = response.Headers.Concat(response.Content.Headers)
                    .ToDictionary(x => x.Key, x => string.Join(", ", x.Value), StringComparer.OrdinalIgnoreCase);
                return new InvocationResult(responsePayload, (int)response.StatusCode, response.ReasonPhrase ?? response.StatusCode.ToString(),
                    response.IsSuccessStatusCode ? string.Empty : responsePayload.Length > 0 ? responsePayload : response.ReasonPhrase ?? string.Empty,
                    JsonConvert.SerializeObject(headers, Formatting.Indented), response.RequestMessage?.RequestUri?.ToString() ?? url);
            }
            catch (HttpRequestException ex)
            {
                return new InvocationResult(string.Empty, 0, string.Empty, ex.Message, string.Empty, url);
            }
        }

        private sealed record InvocationResult(string Payload, int StatusCode, string Status, string ErrorMessage, string ResponseHeaders, string Url);
    }
}
