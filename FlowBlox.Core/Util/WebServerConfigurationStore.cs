using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.WebServer;
using FlowBlox.Core.Validations;
using Newtonsoft.Json;

namespace FlowBlox.Core.Util;

public static class WebServerConfigurationStore
{
    public const string OptionName = "WebServer.Configuration";

    public static WebServerConfiguration Load()
    {
        var raw = FlowBloxOptions.GetOptionInstance().GetOption(OptionName)?.PersistentValue;
        return string.IsNullOrWhiteSpace(raw) ? new WebServerConfiguration() :
            JsonConvert.DeserializeObject<WebServerConfiguration>(raw)
                ?? throw new InvalidDataException("Invalid WebServer.Configuration.");
    }

    public static void Save(WebServerConfiguration configuration)
    {
        var errors = FlowBloxComponentValidator.Validate(configuration);
        if (errors.Count > 0)
            throw new ValidationException(string.Join(Environment.NewLine, errors));
        var options = FlowBloxOptions.GetOptionInstance();
        var option = options.GetOption(OptionName) ?? throw new InvalidOperationException("Web server option is missing.");
        option.PersistentValue = JsonConvert.SerializeObject(configuration);
        options.Save(throwOnError: true);
    }
}
