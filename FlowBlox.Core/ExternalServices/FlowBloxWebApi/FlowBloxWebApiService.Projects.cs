using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System.Text;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi
{
    public partial class FlowBloxWebApiService
    {
        public async Task<ApiResponse<FbProjectResult>> GetProjectAsync(FbProjectRequest request, string userToken = "")
        {
            string query = string.IsNullOrEmpty(request.Name) ? $"guid={request.Guid}" : $"name={request.Name}";
            string url = UriHelper.ConcatUri(_baseUrl, $"project.php?{query}");

            if (!string.IsNullOrEmpty(userToken))
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return new ApiResponse<FbProjectResult>
                    {
                        Success = true,
                        ResultObject = JsonConvert.DeserializeObject<FbProjectResult>(responseBody)
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<FbProjectResult>(response);
                }
            }
            catch (HttpRequestException e)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(e);
                return new ApiResponse<FbProjectResult>
                {
                    Success = false,
                    ErrorMessage = e.Message
                };
            }
        }

        public async Task<ApiResponse<List<FbProjectResult>>> GetProjectsAsync(string userToken = "", bool mine = false, string searchForName = "")
        {
            string url = mine
                ? UriHelper.ConcatUri(_baseUrl, $"projects.php?mine=1&searchForName={searchForName}")
                : UriHelper.ConcatUri(_baseUrl, $"projects.php?searchForName={searchForName}");

            if (!string.IsNullOrEmpty(userToken))
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return new ApiResponse<List<FbProjectResult>>
                    {
                        Success = true,
                        ResultObject = JsonConvert.DeserializeObject<List<FbProjectResult>>(responseBody)
                    };
                }
                else
                {
                    return await CreateErrorApiResponse<List<FbProjectResult>>(response);
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse<List<FbProjectResult>>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse<string>> GetProjectContentAsync(string userToken, Guid projectGuid)
        {
            string url = UriHelper.ConcatUri(_baseUrl, $"project_content.php?guid={projectGuid}");

            if (!string.IsNullOrEmpty(userToken))
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var contentResponse = JsonConvert.DeserializeObject<FbGetProjectContentResponse>(jsonResponse);

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

        public class CreateProjectApiResponse : ApiResponse
        {
            public string ProjectGuid { get; set; }
        }

        public async Task<CreateProjectApiResponse> CreateProjectAsync(string userToken, FbProjectCreateRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "project.php");
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _uploadClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _uploadClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var createResult = JsonConvert.DeserializeObject<FbProjectCreateResult>(responseBody);

                    return new CreateProjectApiResponse
                    {
                        Success = true,
                        ProjectGuid = createResult?.ProjectGuid
                    };
                }
                else
                {
                    var error = await CreateErrorApiResponse(response);
                    return new CreateProjectApiResponse
                    {
                        Success = false,
                        ErrorCode = error.ErrorCode,
                        ErrorMessage = error.ErrorMessage
                    };
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new CreateProjectApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ApiResponse> UpdateProjectAsync(string userToken, FbProjectChangeRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "project.php");
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _uploadClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _uploadClient.PutAsync(url, content);
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

        public async Task<ApiResponse> DeleteProjectAsync(string userToken, FbProjectDeleteRequest request)
        {
            string url = UriHelper.ConcatUri(_baseUrl, "project.php");
            var json = JsonConvert.SerializeObject(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);

            try
            {
                HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
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
    }
}