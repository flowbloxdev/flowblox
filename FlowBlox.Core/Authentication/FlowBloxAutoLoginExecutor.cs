using FlowBlox.Core.Constants;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;

namespace FlowBlox.Core.Authentication
{
    public class FlowBloxAutoLoginExecutor
    {
        public async Task TryAutoLoginAsync()
        {
            try
            {
                var loginDataList = LoadLoginDataList();
                if (loginDataList == null || loginDataList.Count == 0)
                {
                    FlowBloxLogManager.Instance.GetLogger().Info("No stored login data available for auto-login.");
                    return;
                }

                foreach (var entry in loginDataList)
                {
                    var apiUrl = FlowBloxApiUrlNomalizer.Normalize(entry.ApiUrl);
                    if (string.IsNullOrWhiteSpace(apiUrl))
                    {
                        FlowBloxLogManager.Instance.GetLogger().Error("Auto-login skipped: Missing ApiUrl.");
                        continue;
                    }

                    {
                        var service = new FlowBloxWebApiService(apiUrl);

                        var tokenResp = await service.LoginAsync(new FbLoginData
                        {
                            ApiUrl = apiUrl,
                            Username = entry.Username,
                            Password = entry.Password
                        });

                        if (!tokenResp.Success || string.IsNullOrWhiteSpace(tokenResp.ResultObject))
                        {
                            var msg = tokenResp.Success
                                ? $"Auto-login failed for '{apiUrl}': Invalid credentials."
                                : $"Auto-login failed for '{apiUrl}': {tokenResp.ErrorMessage}";

                            FlowBloxLogManager.Instance.GetLogger().Error(msg);
                            continue;
                        }

                        var token = tokenResp.ResultObject;

                        var userDataResp = await service.GetUserInfoAsync(token);
                        if (!userDataResp.Success || userDataResp.ResultObject == null)
                        {
                            var msg = userDataResp.Success
                                ? $"Failed to retrieve user data for '{apiUrl}'."
                                : $"Failed to retrieve user data for '{apiUrl}': {userDataResp.ErrorMessage}";

                            FlowBloxLogManager.Instance.GetLogger().Error(msg);
                            continue;
                        }

                        var userData = userDataResp.ResultObject;

                        FlowBloxAccountManager.Instance.SetSession(apiUrl, userData, token);
                        FlowBloxLogManager.Instance.GetLogger().Info($"Auto-login successful for '{apiUrl}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error($"An error occurred during auto-login: {ex.Message}", ex);
            }
        }

        private static List<FbLoginData> LoadLoginDataList()
        {
            var loginDataString = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AccountLoginData).Value;
            if (string.IsNullOrWhiteSpace(loginDataString))
                return new List<FbLoginData>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<FbLoginData>>(loginDataString);
                return list ?? new List<FbLoginData>();
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to deserialize \"Account.LoginData\" for auto-login.", ex);
                return new List<FbLoginData>();
            }
        }
    }
}
