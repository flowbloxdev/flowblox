namespace FlowBlox.Core.Models.Runtime.Debugging
{
    public sealed class RuntimeDebuggingResult
    {
        public string ProjectName { get; set; } = string.Empty;
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public string TargetFlowBlockName { get; set; }
        public int MaxRuntimeSeconds { get; set; }
        public bool IncludeTargetExecution { get; set; }
        public int MaxCapturedFieldValueChanges { get; set; }
        public bool Aborted { get; set; }
        public RuntimeCancellationContext Cancellation { get; set; }
        public int TotalFieldValueChanges { get; set; }
        public int StoredFieldValueChanges { get; set; }
        public int TotalGeneratedResults { get; set; }
        public List<RuntimeDebugProtocolEntry> Protocol { get; set; } = new();
        public List<RuntimeFieldValueChangeDetails> FieldValueChanges { get; set; } = new();
        public List<RuntimeGeneratedResultDetails> GeneratedResults { get; set; } = new();
        public List<RuntimeDebugProblemEntry> Warnings { get; set; } = new();
        public List<RuntimeDebugProblemEntry> Errors { get; set; } = new();
        public List<RuntimeDebugProblemEntry> WarningNotifications { get; set; } = new();
        public List<RuntimeDebugProblemEntry> ErrorNotifications { get; set; } = new();
        public List<RuntimeDebugPreconditionFailureEntry> PreconditionFailures { get; set; } = new();
    }
}
