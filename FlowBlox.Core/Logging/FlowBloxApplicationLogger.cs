using FlowBlox.Core.Constants;
using log4net;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Logging
{
    public class FlowBloxApplicationLogger : FlowBloxLoggerBase
    {
        private string _applicationId;
        private readonly string _applicationLogDirectory;

        public FlowBloxApplicationLogger(string applicationLogFileName) : base("log4net.application.config")
        {
            if (string.IsNullOrEmpty(applicationLogFileName))
                throw new ArgumentNullException(nameof(applicationLogFileName));

            _applicationId = applicationLogFileName;
            _applicationLogDirectory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsApplicationLogDir)?.Value;
            if (string.IsNullOrWhiteSpace(_applicationLogDirectory))
            {
                _applicationLogDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlowBlox",
                    "logs",
                    "application");
            }

            GlobalContext.Properties["applicationLogFileName"] = _applicationId;
            GlobalContext.Properties["applicationLogDirectory"] = _applicationLogDirectory;

            Configure();
        }

        public override string GetLogfilePath()
        {
            string logFilePath = base.GetLogfilePath();

            if (!string.IsNullOrEmpty(logFilePath))
            {
                logFilePath = logFilePath
                    .Replace("%property{applicationLogFileName}", _applicationId)
                    .Replace("%property{applicationLogDirectory}", _applicationLogDirectory);
            }

            return logFilePath;
        }
    }
}
