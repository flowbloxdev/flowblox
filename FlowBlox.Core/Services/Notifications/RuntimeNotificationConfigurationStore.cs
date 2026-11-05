using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Notifications;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FlowBlox.Core.Services.Notifications
{
    public static class RuntimeNotificationConfigurationStore
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() }
        };

        public static RuntimeNotificationConfiguration Load(out string error)
        {
            error = string.Empty;
            var options = FlowBloxOptions.GetOptionInstance();
            var raw = options.GetOption(OptionsConstants.NotificationsConfiguration)?.Value;
            RuntimeNotificationConfiguration configuration;

            try
            {
                configuration = string.IsNullOrWhiteSpace(raw)
                    ? new RuntimeNotificationConfiguration()
                    : JsonConvert.DeserializeObject<RuntimeNotificationConfiguration>(raw, Settings)
                        ?? new RuntimeNotificationConfiguration();
            }
            catch (Exception ex)
            {
                error = string.Format(FlowBloxTexts.RuntimeNotificationConfiguration_LoadFailed, ex.Message);
                configuration = new RuntimeNotificationConfiguration();
            }

            configuration.EnsureDefaultRules();
            try
            {
                configuration.Password = options.GetOption(OptionsConstants.NotificationsSmtpPassword)?.Value ?? string.Empty;
            }
            catch (Exception ex)
            {
                error = string.Format(FlowBloxTexts.RuntimeNotificationConfiguration_LoadFailed, ex.Message);
                configuration.Password = string.Empty;
            }
            return configuration;
        }

        public static bool Save(RuntimeNotificationConfiguration configuration, out string error)
        {
            error = string.Empty;
            try
            {
                ArgumentNullException.ThrowIfNull(configuration);
                configuration.EnsureDefaultRules();

                var options = FlowBloxOptions.GetOptionInstance();
                var configurationOption = options.GetOption(OptionsConstants.NotificationsConfiguration);
                var passwordOption = options.GetOption(OptionsConstants.NotificationsSmtpPassword);
                if (configurationOption == null || passwordOption == null)
                    throw new InvalidOperationException("Runtime notification options have not been initialized.");

                configurationOption.Value = JsonConvert.SerializeObject(configuration, Settings);
                passwordOption.Value = configuration.Password ?? string.Empty;
                options.Save();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
