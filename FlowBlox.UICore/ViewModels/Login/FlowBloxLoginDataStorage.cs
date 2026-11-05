using FlowBlox.Core.Constants;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System.Windows;

namespace FlowBlox.UICore.ViewModels.Login
{
    public static class FlowBloxLoginDataStorage
    {

        public static List<FbLoginData> LoadLoginDataList(Window ownerWindow)
        {
            var optionValue = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AccountLoginData)?.Value;

            if (string.IsNullOrWhiteSpace(optionValue))
                return new List<FbLoginData>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<FbLoginData>>(optionValue);
                if (list != null)
                    return list;
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to deserialize Account.LoginData.", ex);
            }

            return new List<FbLoginData>();
        }

        public static void UpsertLoginData(FbLoginData data)
        {
            if (data == null)
                return;

            var list = LoadLoginDataList(null);

            var normalizedUrl = FlowBloxApiUrlNomalizer.Normalize(data.ApiUrl);

            var existing = list.FirstOrDefault(x =>
                string.Equals(FlowBloxApiUrlNomalizer.Normalize(x.ApiUrl), normalizedUrl, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                list.Add(new FbLoginData
                {
                    ApiUrl = normalizedUrl,
                    Username = data.Username,
                    Password = data.Password
                });
            }
            else
            {
                existing.ApiUrl = normalizedUrl;
                existing.Username = data.Username;
                existing.Password = data.Password;
            }

            var json = JsonConvert.SerializeObject(list);
            var optionsInst = FlowBloxOptions.GetOptionInstance();
            optionsInst.GetOption(OptionsConstants.AccountLoginData).Value = json;
            optionsInst.Save();
        }
    }
}
