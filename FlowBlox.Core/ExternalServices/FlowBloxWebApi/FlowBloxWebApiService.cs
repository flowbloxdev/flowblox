using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System.Text;
using System.Net;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi
{
    public partial class FlowBloxWebApiService
    {
        protected string _baseUrl;
        protected HttpClient _httpClient;

        /// <summary>
        /// Gets the configured base URL of the FlowBlox Web API.
        /// </summary>
        public string BaseUrl => _baseUrl;

        public FlowBloxWebApiService(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("The endpoint URI must not be null or empty.", nameof(baseUrl));

            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
        }

        protected class ErrorDetails
        {
            public string Type { get; set; }
            public string Message { get; set; }
            public string File { get; set; }
            public int? Line { get; set; }
            public string Trace { get; set; }

        }
        protected class ErrorResponse
        {
            public string Error { get; set; }
            public string ErrorCode { get; set; }
            public string RequestId { get; set; }
            public ErrorDetails Details { get; set; }
        }

        public class ApiResponse
        {
            public bool Success { get; set; }

            public HttpStatusCode? StatusCode { get; set; }

            public string ErrorMessage { get; set; }

            public string ErrorCode { get; set; }
        }

        public class ApiResponse<TResult> : ApiResponse
        {
            public TResult ResultObject { get; set; }
        }

        private ErrorResponse DeserializeOrLogErrorResponse(HttpResponseMessage response, string errorResponse)
        {
            var logger = FlowBloxLogManager.Instance.GetLogger();

            ErrorResponse error = null;
            try
            {
                error = JsonConvert.DeserializeObject<ErrorResponse>(errorResponse);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to deserialize ErrorResponse JSON. HTTP {(int)response.StatusCode}.", ex);
                return null;
            }
            return error;
        }


        private async Task LogErrorApiResponse(HttpResponseMessage response)
        {
            var logger = FlowBloxLogManager.Instance.GetLogger();

            string errorResponse = await response.Content.ReadAsStringAsync();
            logger.Error($"API call failed. HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Raw response: {errorResponse}");

            var error = DeserializeOrLogErrorResponse(response, errorResponse);

            if (error != null)
            {
                logger.Error($"API error received. Error='{error.Error}', ErrorCode='{error.ErrorCode}', RequestId='{error.RequestId}'");
                if (error.Details != null)
                {
                    logger.Error($"API error details: Type='{error.Details.Type}', Message='{error.Details.Message}', File='{error.Details.File}', Line={error.Details.Line}");

                    if (!string.IsNullOrWhiteSpace(error.Details.Trace))
                        logger.Error($"API error stack trace:{Environment.NewLine}{error.Details.Trace}");
                }
            }
        }

        private async Task<ApiResponse> CreateErrorApiResponse(HttpResponseMessage response)
        {
            await LogErrorApiResponse(response);

            string errorResponse = await response.Content.ReadAsStringAsync();
            var error = DeserializeOrLogErrorResponse(response, errorResponse);

            var userMessage = error?.Error;
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                userMessage = string.Join(" ",
                [
                    $"The server returned an error (HTTP {(int)response.StatusCode}).",
                    "Please see application logs for details."
                ]);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(userMessage) &&
                    !userMessage.EndsWith(".") &&
                    !userMessage.EndsWith("!") &&
                    !userMessage.EndsWith("?"))
                {
                    userMessage += ".";
                }

                userMessage = string.Join(" ",
                [
                    $"The server returned an error.",
                    $"Message: {userMessage}",
                    "Please see application logs for details."
                ]);
            }

            return new ApiResponse
            {
                Success = false,
                ErrorCode = error?.ErrorCode,
                StatusCode = response.StatusCode,
                ErrorMessage = userMessage
            };
        }

        private async Task<ApiResponse<TResult>> CreateErrorApiResponse<TResult>(HttpResponseMessage response)
        {
            var errorResponse = await CreateErrorApiResponse(response);

            return new ApiResponse<TResult>
            {
                Success = false,
                ErrorCode = errorResponse.ErrorCode,
                StatusCode = errorResponse.StatusCode,
                ErrorMessage = errorResponse.ErrorMessage,
                ResultObject = default
            };
        }

        public async Task<ApiResponse<FbApiMetadata>> GetApiMetadataAsync()
        {
            string url = UriHelper.ConcatUri(_baseUrl, "apimetadata.php");

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string jsonData = await response.Content.ReadAsStringAsync();
                    var metadata = JsonConvert.DeserializeObject<FbApiMetadata>(jsonData);
                    return new ApiResponse<FbApiMetadata>
                    {
                        Success = true,
                        ResultObject = metadata
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<FbApiMetadata>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<FbApiMetadata>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> Register(FbRegistrationData registrationData)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "register.php");
            try
            {
                var json = JsonConvert.SerializeObject(registrationData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }


        public async Task<ApiResponse<FbCaptchaResponse>> GetCaptchaAsync()
        {
            string url = UriHelper.ConcatUri(_baseUrl, "captcha.php");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonData = await response.Content.ReadAsStringAsync();
                    var captchaResult = JsonConvert.DeserializeObject<CaptchaResult>(jsonData);

                    if (!string.IsNullOrEmpty(captchaResult?.CaptchaImage))
                    {
                        // Entferne den Prefix "data:image/png;base64," falls vorhanden
                        var base64Data = captchaResult.CaptchaImage.Split(',')[1];

                        return new ApiResponse<FbCaptchaResponse>
                        {
                            Success = true,
                            ResultObject = new FbCaptchaResponse
                            {
                                CaptchaId = captchaResult.CaptchaId,
                                CaptchaImageBase64 = base64Data
                            }
                        };
                    }

                    return new ApiResponse<FbCaptchaResponse>
                    {
                        Success = true,
                        ResultObject = null
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<FbCaptchaResponse>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<FbCaptchaResponse>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private class CaptchaResult
        {
            public string CaptchaId { get; set; }
            public string CaptchaImage { get; set; }
        }


        public async Task<ApiResponse<string>> LoginAsync(FbLoginData loginData)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "login.php");
            try
            {
                var json = JsonConvert.SerializeObject(loginData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                string responseData = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonConvert.DeserializeObject<FbTokenResponse>(responseData);
                    return new ApiResponse<string>
                    {
                        Success = true,
                        ResultObject = tokenResponse?.Token
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<string>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<string>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse<FbUserData>> GetUserInfoAsync(string token)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "userinfo.php?token=" + token);
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonData = await response.Content.ReadAsStringAsync();
                    var userInfo = JsonConvert.DeserializeObject<FbUserData>(jsonData);
                    return new ApiResponse<FbUserData>
                    {
                        Success = true,
                        ResultObject = userInfo
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<FbUserData>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<FbUserData>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse<FbUserData>> UpdateUserInfoAsync(string userToken, FbUserChangeRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "userinfo.php");

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PutAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    string jsonData = await response.Content.ReadAsStringAsync();
                    var userInfo = JsonConvert.DeserializeObject<FbUserData>(jsonData);

                    return new ApiResponse<FbUserData>
                    {
                        Success = true,
                        ResultObject = userInfo
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<FbUserData>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<FbUserData>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> GeneratePasswordResetCodeAsync(FbPasswordResetRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "generatePasswordResetCode.php");
            try
            {
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> ChangePasswordAsync(FbChangePasswordRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "changePassword.php");
            try
            {
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse<FbExtensionResult>> GetExtensionAsync(FbExtensionRequest request)
        {
            string query = string.IsNullOrEmpty(request.Name) ? $"guid={request.Guid}" : $"name={request.Name}";
            string url = UriHelper.ConcatUri(_baseUrl, $"extension.php?{query}");

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return new ApiResponse<FbExtensionResult>
                    {
                        Success = true,
                        ResultObject = JsonConvert.DeserializeObject<FbExtensionResult>(responseBody)
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<FbExtensionResult>(response);
                }
            }
            catch (HttpRequestException e)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(e);
                return new ApiResponse<FbExtensionResult>
                {
                    Success = false,
                    ErrorMessage = e.Message
                };
            }
        }

        public async Task<ApiResponse<List<FbExtensionResult>>> GetExtensionsAsync(string userToken = "", string searchForName = "", string searchForUsername = "")
        {
            // Konstruiere die URL basierend auf den vorhandenen Parametern
            string url = string.IsNullOrEmpty(searchForUsername)
                ? UriHelper.ConcatUri(_baseUrl, $"extensions.php?searchForName={searchForName}")
                : UriHelper.ConcatUri(_baseUrl, $"extensions.php?searchForUsername={searchForUsername}");

            if (!string.IsNullOrEmpty(userToken))
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return new ApiResponse<List<FbExtensionResult>>
                    {
                        Success = true,
                        ResultObject = JsonConvert.DeserializeObject<List<FbExtensionResult>>(responseBody)
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<List<FbExtensionResult>>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<List<FbExtensionResult>>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> CreateExtensionAsync(string userToken, FbCreateExtensionRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "extension.php");
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    string resp = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<FbCreateExtensionResult>(resp);
                    return new ApiResponse
                    {
                        Success = true
                    };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }


        public async Task<ApiResponse> CreateExtensionVersionAsync(string userToken, FbCreateExtensionVersionRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "version.php");
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> UpdateExtensionAsync(string userToken, FbExtensionChangeRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "extension.php");
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.PutAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> UpdateExtensionVersionAsync(string userToken, FbExtensionVersionChangeRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "version.php");
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.PutAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse
                    {
                        Success = true
                    };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> DeleteExtensionAsync(string userToken, FbDeleteExtensionRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "extension.php");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                HttpRequestMessage requestMessage = new HttpRequestMessage
                {
                    Content = content,
                    Method = HttpMethod.Delete,
                    RequestUri = new Uri(url)
                };

                HttpResponseMessage response = await _httpClient.SendAsync(requestMessage);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> DeleteExtensionVersionAsync(string userToken, FbDeleteExtensionVersionRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "version.php");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                HttpRequestMessage requestMessage = new HttpRequestMessage
                {
                    Content = content,
                    Method = HttpMethod.Delete,
                    RequestUri = new Uri(url)
                };

                HttpResponseMessage response = await _httpClient.SendAsync(requestMessage);
                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse { Success = true };
                }
                else
                {
                    return await CreateErrorApiResponse(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse<bool>> HasVersionContentAsync(Guid extensionGuid, string version)
        {
            string url = UriHelper.ConcatUri(_baseUrl, $"version_content.php?guid={extensionGuid}&version={version}&mode=hasContent");

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var hasContentResponse = JsonConvert.DeserializeObject<FbHasVersionContentResponse>(jsonResponse);
                    return new ApiResponse<bool>
                    {
                        Success = true,
                        ResultObject = hasContentResponse?.HasContent ?? false
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<bool>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<bool>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse<string>> GetVersionContentAsync(Guid extensionGuid, string version)
        {
            string url = UriHelper.ConcatUri(_baseUrl, $"version_content.php?guid={extensionGuid}&version={version}");

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var contentResponse = JsonConvert.DeserializeObject<FbGetVersionContentResponse>(jsonResponse);
                    return new ApiResponse<string>
                    {
                        Success = true,
                        ResultObject = contentResponse?.Content
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<string>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<string>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
