using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Notifications;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FlowBlox.Core.Services.Notifications
{
    public sealed class RuntimeNotificationOptionsRegistration : IOptionsRegistration
    {
        public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                Converters = 
                { 
                    new StringEnumConverter() 
                }
            };

            defaults.Add(new OptionElement(
                "Notifications.Configuration",
                JsonConvert.SerializeObject(new RuntimeNotificationConfiguration(), settings),
                "JSON configuration for runtime notifications.",
                OptionElement.OptionType.Text));

            defaults.Add(new OptionElement(
                "Notifications.SmtpPassword",
                string.Empty,
                "SMTP password for runtime notifications, protected with Windows DPAPI for the current user.",
                OptionElement.OptionType.Password));
        }
    }
}
