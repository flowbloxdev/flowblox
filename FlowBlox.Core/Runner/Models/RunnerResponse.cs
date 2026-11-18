using FlowBlox.Core.Models.Runtime.Debugging;

using FlowBlox.Core.Models.Runtime;

namespace FlowBlox.Core.Runner.Contracts
{
    public sealed class RunnerResponse
    {
        public RunnerResponse()
        {
            Warnings = new List<string>();
            Outputs = new(StringComparer.OrdinalIgnoreCase);
        }

        public bool Success { get; set; }
        public int ExitCode { get; set; }

        public List<string> Warnings { get; set; }
        public List<ExecutionEnvironmentPrerequisiteFailure> ExecutionEnvironmentPrerequisiteFailures { get; set; } = new();
        public string ErrorMessage { get; set; }
        public string Exception { get; set; }
        public string AbortMessage { get; set; }
        public string AbortException { get; set; }

        public string ProjectName { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public string LogfilePath { get; set; }
        public string DebuggingResultFilePath { get; set; }
        public RuntimeCancellationKind? CancellationKind { get; set; }
        public string CancellationReason { get; set; }

        public Dictionary<string, List<ProjectOutputDatasetDto>> Outputs { get; set; }
    }
}
