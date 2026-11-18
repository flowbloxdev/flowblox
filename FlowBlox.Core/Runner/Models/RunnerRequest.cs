using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Runner.Contracts
{
    public sealed class RunnerRequest
    {
        public string ProjectFile { get; set; }
        public string ProjectSpaceGuid { get; set; }
        public int? ProjectSpaceVersion { get; set; }
        public Dictionary<string, string> UserFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? PasswordFieldNames { get; set; }
        public Dictionary<string, string> OptionOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AutoRestart { get; set; } = false;
        public bool AbortOnError { get; set; } = true;
        public bool AbortOnWarning { get; set; } = false;
        public RunnerExternalDebuggingInformation ExternalDebuggingInformation { get; set; }
    }
}
